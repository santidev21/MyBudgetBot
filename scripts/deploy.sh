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
# restarted, so a failed deployment can always return to a known good state. The
# pre-deploy dump lands in the same backup volume the nightly job uses, and is
# verified with pg_restore before the build starts.
#
set -euo pipefail

DEPLOY_DIR="${DEPLOY_DIR:-/opt/mybudget}"
HEALTH_URL="${HEALTH_URL:-http://localhost:8080/health/ready}"
COMPOSE="docker compose"
LOG_FILE="/tmp/mybudget-deploy.log"
PREDEPLOY_KEEP="${BACKUP_PREDEPLOY_RETENTION:-5}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/backup-lib.sh
source "$SCRIPT_DIR/lib/backup-lib.sh"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" | tee -a "$LOG_FILE"; }

# Terminal failure: there is no safe way to continue.
fail() { log "ERROR: $*"; exit 1; }

# Recoverable failure: the caller decides, usually by rolling back.
bail() { log "ERROR: $*"; return 1; }

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

    if ! database_is_running; then
        log "Database container is not running; skipping the pre-deploy backup."
        return 0
    fi

    log "Backing up the database before deploy..."
    BACKUP_FILE="pre-deploy-$(date +%Y%m%d-%H%M%S).dump"

    dump_database "$BACKUP_FILE" >/dev/null
    verify_dump_readable "$BACKUP_FILE"
    prune_prefix "pre-deploy" "$PREDEPLOY_KEEP"

    log "Verified pre-deploy backup $BACKUP_FILE in the $BACKUP_VOLUME volume."
}

pull() {
    log "Pulling the latest main..."
    cd "$DEPLOY_DIR"
    git fetch --prune origin main
    git reset --hard origin/main

    # A stray untracked file can change the build or the compose config. Tracked
    # changes are already gone after the reset; ignored files (like .env) are
    # left alone on purpose.
    git clean -fd >/dev/null
    [ -z "$(git status --porcelain)" ] \
        || fail "The working tree is not clean after pulling origin/main."
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

    if [ -n "${BACKUP_FILE:-}" ]; then
        log "A verified database dump from before this deployment is at $BACKUP_FILE"
        log "in the $BACKUP_VOLUME volume. Restore it only if the previous release"
        log "needs the older schema: CONFIRM_RESTORE=RESTORE ./scripts/restore.sh $BACKUP_FILE"
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
