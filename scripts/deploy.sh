#!/usr/bin/env bash
#
# MyBudget-bot production deployment.
#
#   ./scripts/deploy.sh deploy     validate, back up, pull, build, up, verify
#   ./scripts/deploy.sh status     container states
#   ./scripts/deploy.sh logs       follow application logs
#   ./scripts/deploy.sh verify     check health endpoints
#   ./scripts/deploy.sh rollback   return to the previous commit
#
# Everything is validated and the database is dumped before anything is rebuilt or
# restarted, so a failed deployment can always return to a known good state.
#
set -euo pipefail

DEPLOY_DIR="${DEPLOY_DIR:-/opt/mybudget}"
BACKUP_DIR="${BACKUP_DIR:-/opt/mybudget/backups}"
HEALTH_URL="${HEALTH_URL:-http://localhost:8080/health/ready}"
COMPOSE="docker compose"
LOG_FILE="/tmp/mybudget-deploy.log"
POSTGRES_IMAGE="postgres:16-alpine"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" | tee -a "$LOG_FILE"; }

# Terminal failure: there is no safe way to continue.
fail() { log "ERROR: $*"; exit 1; }

# Recoverable failure: the caller decides, usually by rolling back.
bail() { log "ERROR: $*"; return 1; }

env_value() {
    # Reads a value from .env without executing it.
    grep -E "^$1=" "$DEPLOY_DIR/.env" | head -n 1 | cut -d= -f2-
}

validate_docker() {
    log "Validating Docker..."
    docker info >/dev/null 2>&1 || fail "Docker daemon is not reachable."
}

validate_env() {
    log "Validating .env..."
    [ -f "$DEPLOY_DIR/.env" ] || fail ".env not found at $DEPLOY_DIR/.env"

    if grep -q "CHANGE_ME" "$DEPLOY_DIR/.env"; then
        fail ".env still contains CHANGE_ME placeholders."
    fi

    local required
    for required in POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD \
                    DB_APP_USER DB_APP_PASSWORD DB_MIGRATOR_USER DB_MIGRATOR_PASSWORD; do
        [ -n "$(env_value "$required")" ] || fail ".env is missing $required"
    done
}

validate_worktree() {
    log "Checking the working tree..."
    cd "$DEPLOY_DIR"
    if git ls-files --error-unmatch .env >/dev/null 2>&1; then
        fail ".env is tracked by git. Run: git rm --cached .env"
    fi
}

validate_config() {
    log "Validating compose configuration..."
    cd "$DEPLOY_DIR"
    $COMPOSE config --quiet || fail "docker compose config validation failed."
}

backup_database() {
    BACKUP_FILE=""

    if ! docker ps --format '{{.Names}}' | grep -q '^mybudget-db$'; then
        log "Database container is not running; skipping the pre-deploy backup."
        return 0
    fi

    log "Backing up the database..."
    mkdir -p "$BACKUP_DIR"
    local target="$BACKUP_DIR/pre-deploy-$(date +%Y%m%d-%H%M%S).dump"

    docker exec mybudget-db pg_dump \
        --format=custom --no-owner --no-privileges \
        --username "$(env_value POSTGRES_USER)" \
        --dbname "$(env_value POSTGRES_DB)" > "$target" \
        || fail "pg_dump failed; refusing to deploy."

    # Verified on the host, where the dump actually lives.
    docker run --rm -v "$BACKUP_DIR:/backups:ro" "$POSTGRES_IMAGE" \
        pg_restore --list "/backups/$(basename "$target")" >/dev/null 2>&1 \
        || fail "The backup is not readable by pg_restore; refusing to deploy."

    BACKUP_FILE="$target"
    log "Verified backup written to $BACKUP_FILE"
}

pull() {
    log "Pulling the latest main..."
    cd "$DEPLOY_DIR"
    git fetch --prune origin main
    git reset --hard origin/main
}

build() {
    log "Building images..."
    cd "$DEPLOY_DIR"
    $COMPOSE build --no-cache
}

up() {
    log "Starting services..."
    cd "$DEPLOY_DIR"
    # The migrator is a one-shot service; removing it forces it to run again.
    docker rm -f mybudget-migrator >/dev/null 2>&1 || true
    $COMPOSE up -d --remove-orphans
}

wait_healthy() {
    log "Waiting for the application to become healthy..."
    local status
    for _ in $(seq 1 60); do
        status=$(docker inspect --format '{{.State.Health.Status}}' mybudget 2>/dev/null || echo "starting")
        if [ "$status" = "healthy" ]; then
            log "Application is healthy."
            return 0
        fi
        sleep 5
    done
    return 1
}

verify() {
    log "Verifying health endpoints..."
    if ! docker exec mybudget curl --fail --silent "$HEALTH_URL" >/dev/null; then
        return 1
    fi

    cd "$DEPLOY_DIR"
    $COMPOSE ps
    log "Readiness check passed."
    return 0
}

deploy() {
    validate_docker
    validate_env
    validate_worktree
    validate_config
    backup_database
    pull
    build

    up || fail "Failed to start services."

    if ! wait_healthy || ! verify; then
        log "Deployment verification failed; rolling back."
        rollback
    fi

    log "Deployment finished successfully."
}

rollback() {
    log "Rolling back to the previous commit..."
    cd "$DEPLOY_DIR"
    git reset --hard HEAD~1 || fail "No previous commit is available for rollback."

    $COMPOSE build
    docker rm -f mybudget-migrator >/dev/null 2>&1 || true
    $COMPOSE up -d --remove-orphans

    if [ -n "${BACKUP_FILE:-}" ] && [ -f "${BACKUP_FILE}" ]; then
        log "A database dump from before this deployment is at $BACKUP_FILE."
        log "Restore it manually only if the previous release needs the older schema."
    fi

    fail "Rollback finished. Investigate before redeploying."
}

case "${1:-deploy}" in
    deploy) deploy ;;
    status) cd "$DEPLOY_DIR" && $COMPOSE ps ;;
    logs) cd "$DEPLOY_DIR" && $COMPOSE logs -f app ;;
    verify)
        if verify; then log "Verification passed."; else fail "Verification failed."; fi
        ;;
    rollback) rollback ;;
    *)
        echo "Usage: $0 [deploy|status|logs|verify|rollback]"
        exit 1
        ;;
esac
