#!/usr/bin/env bash
#
# Update the Restaurant Call Center System to a new version, safely.
# Runs on the server (see docs/DEPLOY-server-runbook.md, "Updating later").
#
#   ./update.sh v1.2            update to v1.2
#   ./update.sh v1.2 --no-backup  skip the pre-update backup (not recommended)
#   ./update.sh --rollback      go straight back to the previous version
#
# Expects /opt/callcenter/callcenter-api-<version>.tar to already be here,
# copied from the development machine:
#
#   # on your PC
#   docker build -f src/CallCenter.Server/Dockerfile -t callcenter-api:v1.2 .
#   docker save callcenter-api:v1.2 -o callcenter-api-v1.2.tar
#   scp callcenter-api-v1.2.tar admin@<server>:/opt/callcenter/
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

for arg in "$@"; do
    case "$arg" in
        --rollback)   ROLLBACK=true ;;
        --no-backup)  DO_BACKUP=false ;;
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
[ -f "$TARBALL" ] || fail "image file not found: $TARBALL
Copy it from your development machine first:
  scp $IMAGE-$VERSION.tar admin@\$(hostname -I | awk '{print \$1}'):$APP_DIR/"

log "Updating to $VERSION"

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

# ------------------------------------------------------ 3. load the new one
log "Loading $TARBALL"
docker load -i "$TARBALL" >/dev/null || fail "docker load failed"

docker image inspect "$IMAGE:$VERSION" >/dev/null 2>&1 \
    || fail "$TARBALL did not contain $IMAGE:$VERSION - was it saved with that tag?"

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
