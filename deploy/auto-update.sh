#!/usr/bin/env bash
#
# Install a new release by itself, at night (see docs/RELEASING.md, "deploy").
# Run by callcenter-auto-update.timer; safe to run by hand.
#
#   ./auto-update.sh            update if a newer release is published
#   ./auto-update.sh --check    say what it would do; downloads, installs nothing
#
# GitHub cannot reach the server, so the server asks: it lists the versions in
# the registry, and when one is newer than what is running it hands it to
# update.sh --pull, which backs up, checks /health/ready and rolls back.
#
# What a release's tag message says decides what happens (release.yml turns it
# into labels on the image):
#
#   deploy: manual   not installed by this script. It stops below that
#                    version and logs every night until someone deploys it by
#                    hand - for a release that needs a new .env value, or the
#                    laptops updated the same day.
#   deploy: seed     runs the seed once after the update (new menu items).
#
# Only plain versions (v1.2.3) count; test builds (v1.2-test) are ignored, and
# a server running a test build is left alone.
#
# Each release carries its own deploy files (docker-compose.yml, update.sh,
# backup.sh and this script) in /deploy in the image. They are copied here
# before the update, and put back if the update rolls back.
#
# Needs the one-time `docker login ghcr.io` that update.sh --pull needs.

set -Eeuo pipefail

APP_DIR="${APP_DIR:-/opt/callcenter}"
IMAGE="${IMAGE:-callcenter-api}"
OWNER="${OWNER:-dianawawreh35-cs}"
REGISTRY="${REGISTRY:-ghcr.io/$OWNER}"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.yml}"
DOCKER_CONFIG_FILE="${DOCKER_CONFIG_FILE:-${DOCKER_CONFIG:-$HOME/.docker}/config.json}"
# The files a release may change on the server. .env is never among them.
DEPLOY_FILES="docker-compose.yml update.sh backup.sh auto-update.sh"

log()  { printf '%s  %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"; }
fail() { log "ERROR: $*"; exit 1; }

CHECK=false
for arg in "$@"; do
    case "$arg" in
        --check)    CHECK=true ;;
        -h|--help)  sed -n '3,29p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *)          fail "unknown option: $arg" ;;
    esac
done

cd "$APP_DIR" || fail "application directory not found: $APP_DIR"
mkdir -p backups

# One run at a time: a run by hand while the timer's is going would be two
# updates on top of each other.
exec 9>"backups/auto-update.lock"
flock -n 9 || { log "Another auto-update is running; nothing to do"; exit 0; }

is_release() { printf '%s' "$1" | grep -Eq '^v[0-9]+\.[0-9]+\.[0-9]+$'; }

# True when version $1 sorts after $2.
newer() { [ "$1" != "$2" ] && [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | tail -n 1)" = "$1" ]; }

label() {
    docker image inspect "$1" --format "{{ index .Config.Labels \"$2\" }}" 2>/dev/null || true
}

# ------------------------------------------------------ what is running now
CURRENT=$(label "$IMAGE:latest" org.opencontainers.image.version)
[ -n "$CURRENT" ] || fail "cannot tell which version is running ($IMAGE:latest has no version label)"
if ! is_release "$CURRENT"; then
    log "Running $CURRENT, which is not a release; leaving it alone"
    exit 0
fi

# ------------------------------------------------ what the registry has
# The registry wants a short-lived token, which it gives for the login that
# `docker login` saved. Docker keeps that as base64 "user:token".
AUTH=$(sed -n '/"ghcr.io"/,/}/ s/.*"auth": *"\([^"]*\)".*/\1/p' "$DOCKER_CONFIG_FILE" 2>/dev/null | head -n 1)
[ -n "$AUTH" ] || fail "no ghcr.io login in $DOCKER_CONFIG_FILE. Log in once, as the user this runs as:
  read -s T && echo \"\$T\" | docker login ghcr.io -u $OWNER --password-stdin; unset T"

BEARER=$(curl -fsS --max-time 20 -H "Authorization: Basic $AUTH" \
        "https://ghcr.io/token?service=ghcr.io&scope=repository:$OWNER/$IMAGE:pull" \
    | sed -n 's/.*"token": *"\([^"]*\)".*/\1/p') \
    || fail "could not sign in to ghcr.io - no internet, or the server's token has expired or been revoked"
[ -n "$BEARER" ] || fail "ghcr.io gave no token - the server's token has expired or been revoked"

TAGS=$(curl -fsS --max-time 20 -H "Authorization: Bearer $BEARER" \
        "https://ghcr.io/v2/$OWNER/$IMAGE/tags/list?n=1000" \
    | tr ',[]{}' '\n\n\n\n\n' | tr -d ' "' | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sort -V) \
    || fail "could not list the versions in $REGISTRY/$IMAGE"

PENDING=""
for tag in $TAGS; do
    if newer "$tag" "$CURRENT"; then PENDING="$PENDING $tag"; fi
done

if [ -z "$PENDING" ]; then
    log "Up to date on $CURRENT"
    exit 0
fi
log "Running $CURRENT; newer in the registry:$PENDING"

# ------------------------------------------------ how far it may go alone
# Every version on the way is read, not only the newest: a "deploy: manual"
# between here and there still needs its manual step, and a "deploy: seed"
# still needs its seed.
TARGET=""
BLOCKED=""
SEED=false
for tag in $PENDING; do
    docker pull -q "$REGISTRY/$IMAGE:$tag" >/dev/null || fail "docker pull $REGISTRY/$IMAGE:$tag failed"
    if [ "$(label "$REGISTRY/$IMAGE:$tag" callcenter.deploy.manual)" = "true" ]; then
        BLOCKED="$tag"
        break
    fi
    [ "$(label "$REGISTRY/$IMAGE:$tag" callcenter.deploy.seed)" = "true" ] && SEED=true
    TARGET="$tag"
done

if [ -n "$BLOCKED" ]; then
    log "$BLOCKED is marked for a manual deploy (its tag message says deploy: manual)."
    log "  Deploy it by hand, as in docs/RELEASING.md; nothing after it installs until then."
fi
if [ -z "$TARGET" ]; then
    exit 0
fi

log "Will update $CURRENT -> $TARGET$([ "$SEED" = true ] && echo ', then run the seed')"
if [ "$CHECK" = true ]; then
    log "--check: nothing changed"
    exit 0
fi

# ------------------------------------------------ the release's deploy files
STAGE=$(mktemp -d)
SAVED="backups/deploy-$CURRENT-$(date '+%Y%m%d-%H%M%S')"
CHANGED=""
trap 'rm -rf "$STAGE"' EXIT

CID=$(docker create "$REGISTRY/$IMAGE:$TARGET")
if docker cp "$CID:/deploy/." "$STAGE/" 2>/dev/null; then
    for f in $DEPLOY_FILES; do
        [ -f "$STAGE/$f" ] || continue
        if [ -f "$f" ] && cmp -s "$STAGE/$f" "$f"; then continue; fi
        mkdir -p "$SAVED"
        [ -f "$f" ] && cp -p "$f" "$SAVED/$f"
        CHANGED="$CHANGED $f"
    done
else
    log "$TARGET carries no deploy files; keeping the ones here"
fi
docker rm "$CID" >/dev/null

# mv, not cp: this script may be among them, and bash is still reading it.
install_files() {   # $1 = the folder to take them from
    local f
    for f in $CHANGED; do
        [ -f "$1/$f" ] || continue
        cp -p "$1/$f" "$f.new"
        case "$f" in *.sh) chmod +x "$f.new" ;; esac
        mv -f "$f.new" "$f"
    done
}

if [ -n "$CHANGED" ]; then
    log "Deploy files changed in $TARGET:$CHANGED (the old ones are in $SAVED)"
    install_files "$STAGE"
fi

# ------------------------------------------------------------------ update
if ./update.sh "$TARGET" --pull; then
    log "Updated $CURRENT -> $TARGET"
else
    # update.sh rolled back if it could. When the old version is what runs
    # again, the old deploy files go back with it.
    NOW=$(label "$IMAGE:latest" org.opencontainers.image.version)
    if [ -n "$CHANGED" ] && [ "$NOW" = "$CURRENT" ]; then
        install_files "$SAVED"
        log "Put back the deploy files of $CURRENT"
    fi
    fail "the update to $TARGET did not go through; update.sh's lines above say why. Still running: ${NOW:-unknown}"
fi

# -------------------------------------------------------------------- seed
if [ "$SEED" = true ]; then
    log "Running the seed ($TARGET's tag message asked for it)"
    docker compose -f "$COMPOSE_FILE" exec -T api dotnet CallCenter.Server.dll seed \
        || fail "$TARGET is live, but the seed failed. Run it by hand:
  docker compose exec api dotnet CallCenter.Server.dll seed"
fi

# The registry copies of versions passed over on the way.
for tag in $PENDING; do
    [ "$tag" = "$TARGET" ] || docker rmi "$REGISTRY/$IMAGE:$tag" >/dev/null 2>&1 || true
done

log "Done: $TARGET is live"
