#!/usr/bin/env bash
#
# MyBudget-bot manual restore into the live database.
#
#   CONFIRM_RESTORE=RESTORE ./scripts/restore.sh <dump-name>
#   CONFIRM_RESTORE=RESTORE ./scripts/restore.sh --latest
#
# This is the break-glass procedure, not an everyday command. It stops the
# application, drops the live database, restores the dump as the migrator role,
# re-applies the least-privilege grants, and brings the stack back up.
#
# Restoring as the migrator (not the superuser) keeps the table ownership that
# the role model expects, and the grants are re-applied because the dump is taken
# with --no-owner --no-privileges on purpose. See docs/BACKUPS.md.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/backup-lib.sh
source "$SCRIPT_DIR/lib/backup-lib.sh"

COMPOSE="docker compose"
LOG_FILE="${RESTORE_LOG_FILE:-/tmp/mybudget-restore.log}"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" | tee -a "$LOG_FILE"; }
fail() { log "RestoreFailed $*"; exit 1; }

app_user() { env_value DB_APP_USER; }

migrator_user() { env_value DB_MIGRATOR_USER; }

stop_application() {
    log "Stopping the application and migrator..."
    docker rm -f mybudget-migrator >/dev/null 2>&1 || true
    docker stop mybudget >/dev/null 2>&1 || true
}

reapply_grants() {
    log "Re-applying role ownership and grants..."

    local database migrator app
    database="$(postgres_database)"
    migrator="$(migrator_user)"
    app="$(app_user)"

    docker exec -i "$DB_CONTAINER" psql \
        --username "$(postgres_owner)" --dbname "$database" \
        --set=ON_ERROR_STOP=1 \
        --variable=migrator_user="$migrator" \
        --variable=app_user="$app" <<-'SQL'
	GRANT CONNECT ON DATABASE :"DBNAME" TO :"migrator_user", :"app_user";
	ALTER SCHEMA public OWNER TO :"migrator_user";
	GRANT USAGE ON SCHEMA public TO :"app_user";
	REVOKE CREATE ON SCHEMA public FROM PUBLIC;
	GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"app_user";
	GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO :"app_user";
	ALTER DEFAULT PRIVILEGES FOR ROLE :"migrator_user" IN SCHEMA public
	    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app_user";
	ALTER DEFAULT PRIVILEGES FOR ROLE :"migrator_user" IN SCHEMA public
	    GRANT USAGE, SELECT ON SEQUENCES TO :"app_user";
	SQL
}

start_application() {
    log "Starting the stack..."
    cd "$DEPLOY_DIR"
    $COMPOSE up -d --remove-orphans
}

wait_healthy() {
    local status
    for _ in $(seq 1 60); do
        status=$(docker inspect --format '{{.State.Health.Status}}' mybudget 2>/dev/null || echo "starting")
        if [ "$status" = "healthy" ]; then
            return 0
        fi
        sleep 5
    done

    return 1
}

main() {
    local name="${1:-}"

    if [ "${CONFIRM_RESTORE:-}" != "RESTORE" ]; then
        fail "Refusing to overwrite the live database. Set CONFIRM_RESTORE=RESTORE to proceed."
    fi

    [ -f "$DEPLOY_DIR/.env" ] || fail ".env not found at $DEPLOY_DIR/.env"
    require_database_container

    if [ "$name" = "--latest" ] || [ -z "$name" ]; then
        name="$(latest_dump mybudget)"
    fi
    [ -n "$name" ] || fail "No backup found to restore."
    docker exec "$DB_CONTAINER" test -f "/backups/$name" \
        || fail "Backup $name does not exist in the volume."

    verify_dump_readable "$name"

    local database migrator
    database="$(postgres_database)"
    migrator="$(migrator_user)"

    log "Restoring $name into $database (this destroys the current contents)."
    stop_application

    docker exec "$DB_CONTAINER" dropdb --if-exists --force \
        --username "$(postgres_owner)" "$database" \
        || fail "Could not drop the $database database."
    docker exec "$DB_CONTAINER" createdb --owner "$migrator" \
        --username "$(postgres_owner)" "$database" \
        || fail "Could not recreate the $database database."

    docker exec "$DB_CONTAINER" pg_restore \
        --no-owner --no-privileges \
        --exit-on-error \
        --username "$migrator" --dbname "$database" \
        "/backups/$name" \
        || fail "pg_restore failed; the database is now empty and needs another attempt."

    reapply_grants

    start_application

    if wait_healthy; then
        log "Restore of $name finished and the application is healthy."
    else
        fail "The application did not become healthy after restoring $name."
    fi
}

main "$@"
