#!/usr/bin/env bash
#
# Update the Restaurant Call Center System to a new version, safely.
# Runs on the server (see docs/DEPLOY-server-runbook.md, "Updating later").
#
#   ./update.sh v1.2            update to v1.2 from a .tar copied here
#   ./update.sh v1.2 --pull     update to v1.2 by pulling from the registry
#   ./update.sh v1.2 --no-backup  skip the pre-update backup (not recommended)
#   ./update.sh --rollback      go straight back to the previous version
#
# Two ways to get the image, pick whichever suits the site's network:
#
#   A. carried here (default) - no internet needed on the server.
#      Expects /opt/callcenter/callcenter-api-<version>.tar, from your PC:
#        docker pull ghcr.io/<owner>/callcenter-api:v1.2
#        docker tag  ghcr.io/<owner>/callcenter-api:v1.2 callcenter-api:v1.2
#        docker save callcenter-api:v1.2 -o callcenter-api-v1.2.tar
#        scp callcenter-api-v1.2.tar admin@<server>:/opt/callcenter/
#
#   B. --pull - the server fetches it itself. Needs internet here, and a
#      one-time login because the image is private:
#        echo $GITHUB_TOKEN | docker login ghcr.io -u <user> --password-stdin
#
# What it does:
#   1. backs up the database and recordings
#   2. tags the running image as :previous so a rollback is always possible
#   3. loads the new image and points :latest at it
#   4. restarts the API container
#   5. waits for /health, and rolls back automatically if it never comes up
#
# The database container and all data are untouched.

set -Eeuo pipefail

APP_DIR="${APP_DIR:-/opt/callcenter}"
IMAGE="${IMAGE:-callcenter-api}"
REGISTRY="${REGISTRY:-ghcr.io/dianawawreh35-cs}"   # only used by --pull
HEALTH_URL="${HEALTH_URL:-http://localhost:5000/health}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-90}"   # seconds to wait for the API to come up
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.yml}"

log()  { printf '%s  %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"; }
fail() { log "ERROR: $*"; exit 1; }

usage() {
    sed -n '3,25p' "$0" | sed 's/^# \{0,1\}//'
    exit "${1:-1}"
}

# ---------------------------------------------------------------- arguments
VERSION=""
DO_BACKUP=true
ROLLBACK=false
PULL=false

for arg in "$@"; do
    case "$arg" in
        --rollback)   ROLLBACK=true ;;
        --no-backup)  DO_BACKUP=false ;;
        --pull)       PULL=true ;;
        -h|--help)    usage 0 ;;
        -*)           fail "unknown option: $arg" ;;
        *)            VERSION="$arg" ;;
    esac
done

cd "$APP_DIR" || fail "application directory not found: $APP_DIR"
[ -f "$COMPOSE_FILE" ] || fail "$COMPOSE_FILE not found in $APP_DIR"

# ---------------------------------------------------------------- rollback
if [ "$ROLLBACK" = true ]; then
    docker image inspect "$IMAGE:previous" >/dev/null 2>&1 \
        || fail "no $IMAGE:previous image - nothing to roll back to"

    log "Rolling back to the previous version"
    docker tag "$IMAGE:previous" "$IMAGE:latest"
    docker compose -f "$COMPOSE_FILE" up -d api
    log "Rolled back. Check: docker compose logs -f api"
    exit 0
fi

[ -n "$VERSION" ] || usage 1

TARBALL="$APP_DIR/$IMAGE-$VERSION.tar"

if [ "$PULL" = false ] && [ ! -f "$TARBALL" ]; then
    fail "image file not found: $TARBALL
Either copy it from your development machine:
  scp $IMAGE-$VERSION.tar admin@\$(hostname -I | awk '{print \$1}'):$APP_DIR/
or pull it from the registry instead:
  ./update.sh $VERSION --pull"
fi

log "Updating to $VERSION ($([ "$PULL" = true ] && echo "pulling from $REGISTRY" || echo "from $(basename "$TARBALL")"))"

# ---------------------------------------------------------------- 1. backup
if [ "$DO_BACKUP" = true ]; then
    [ -x ./backup.sh ] || fail "backup.sh not found or not executable (use --no-backup to skip)"
    log "Backing up before the update"
    ./backup.sh || fail "backup failed - update aborted, nothing has changed"
else
    log "WARNING: skipping the pre-update backup"
fi

# ---------------------------------------------- 2. remember what is running
if docker image inspect "$IMAGE:latest" >/dev/null 2>&1; then
    docker tag "$IMAGE:latest" "$IMAGE:previous"
    log "Current version tagged as $IMAGE:previous"
    HAVE_PREVIOUS=true
else
    log "No existing $IMAGE:latest - this looks like a first install"
    HAVE_PREVIOUS=false
fi

# ------------------------------------------------------ 3. get the new one
if [ "$PULL" = true ]; then
    log "Pulling $REGISTRY/$IMAGE:$VERSION"
    docker pull "$REGISTRY/$IMAGE:$VERSION" || fail "docker pull failed.
If this is an authentication error, the image is private - log in once:
  echo \$GITHUB_TOKEN | docker login ghcr.io -u <your-github-user> --password-stdin"
    docker tag "$REGISTRY/$IMAGE:$VERSION" "$IMAGE:$VERSION"
else
    log "Loading $TARBALL"
    docker load -i "$TARBALL" >/dev/null || fail "docker load failed"
fi

docker image inspect "$IMAGE:$VERSION" >/dev/null 2>&1 \
    || fail "$IMAGE:$VERSION is not present after the $([ "$PULL" = true ] && echo pull || echo load) - was the image tagged with that version?"

docker tag "$IMAGE:$VERSION" "$IMAGE:latest"

# --------------------------------------------------------------- 4. restart
log "Restarting the API"
docker compose -f "$COMPOSE_FILE" up -d api || fail "docker compose up failed"

# ----------------------------------------------------------- 5. verify
log "Waiting for $HEALTH_URL (up to ${HEALTH_TIMEOUT}s)"
deadline=$(( $(date +%s) + HEALTH_TIMEOUT ))
healthy=false

while [ "$(date +%s)" -lt "$deadline" ]; do
    if curl -fsS --max-time 5 "$HEALTH_URL" >/dev/null 2>&1; then
        healthy=true
        break
    fi
    sleep 3
done

if [ "$healthy" = true ]; then
    log "$VERSION is live and healthy"
    log "Logs: docker compose logs -f api"
    exit 0
fi

# ------------------------------------------------------- 5b. roll back
log "ERROR: the API did not become healthy within ${HEALTH_TIMEOUT}s"
docker compose -f "$COMPOSE_FILE" logs --tail 40 api || true

if [ "$HAVE_PREVIOUS" = true ]; then
    log "Rolling back to the previous version"
    docker tag "$IMAGE:previous" "$IMAGE:latest"
    docker compose -f "$COMPOSE_FILE" up -d api

    if curl -fsS --max-time 5 --retry 10 --retry-delay 3 "$HEALTH_URL" >/dev/null 2>&1; then
        log "Rolled back successfully - the previous version is serving again"
    else
        log "ROLLBACK ALSO UNHEALTHY - the system is down, investigate now:"
        log "  docker compose -f $COMPOSE_FILE logs --tail 100 api"
    fi
else
    log "No previous version to roll back to - the system is down"
fi

exit 1
