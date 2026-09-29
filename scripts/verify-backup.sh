#!/usr/bin/env bash
#
# MyBudget-bot restore drill.
#
#   ./scripts/verify-backup.sh            # newest daily dump
#   ./scripts/verify-backup.sh <name>     # a specific dump file name
#
# Restores a dump into a throwaway database, runs sanity checks against the
# restored data, and drops the database again. The live database is never
# touched. Any failure alerts the admin through scripts/lib/backup-lib.sh.
#
# This is the step that turns "a dump exists" into "a backup exists".
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/backup-lib.sh
source "$SCRIPT_DIR/lib/backup-lib.sh"

LOG_FILE="${VERIFY_LOG_FILE:-/tmp/mybudget-verify.log}"
CHECK_DB="${RESTORE_CHECK_DB:-mybudget_restore_check}"

# The design names this event; keeping it stable lets the ops log be searched.
LOG_EVENT="BackupVerificationFailed"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" | tee -a "$LOG_FILE"; }
fail() {
    log "$LOG_EVENT $*"
    notify_admin "⚠️ La verificación del respaldo de MyBudget falló: $*"
    exit 1
}

drop_check_database() {
    docker exec "$DB_CONTAINER" \
        dropdb --if-exists --username "$(postgres_owner)" "$CHECK_DB" >/dev/null 2>&1 || true
}

# Raises an exception on the first broken invariant, so ON_ERROR_STOP aborts and
# the command exits non-zero. The final SELECTs are the drill's paper trail.
sanity_sql() {
    cat <<-'SQL'
	DO $$
	BEGIN
	    IF (SELECT count(*) FROM "__EFMigrationsHistory") = 0 THEN
	        RAISE EXCEPTION 'the restored database has no applied migrations';
	    END IF;

	    IF EXISTS (SELECT 1 FROM expenses WHERE amount <= 0) THEN
	        RAISE EXCEPTION 'expenses with non-positive amounts found';
	    END IF;

	    IF EXISTS (SELECT 1 FROM expenses WHERE expense_date IS NULL) THEN
	        RAISE EXCEPTION 'expenses without a calendar date found';
	    END IF;

	    IF EXISTS (SELECT 1 FROM users WHERE telegram_user_id IS NULL) THEN
	        RAISE EXCEPTION 'users without a telegram id found';
	    END IF;
	END $$;

	SELECT 'users=' || count(*) FROM users;
	SELECT 'expenses=' || count(*) FROM expenses;
	SELECT 'newest_expense_date=' || coalesce(max(expense_date)::text, 'none') FROM expenses;
	SQL
}

restore_into_check_db() {
    local name="$1"

    docker exec "$DB_CONTAINER" pg_restore \
        --no-owner --no-privileges \
        --exit-on-error \
        --username "$(postgres_owner)" \
        --dbname "$CHECK_DB" \
        "/backups/$name"
}

run_sanity_checks() {
    docker exec -i "$DB_CONTAINER" psql \
        --username "$(postgres_owner)" \
        --dbname "$CHECK_DB" \
        --quiet --set=ON_ERROR_STOP=1 --tuples-only --no-align <<< "$(sanity_sql)"
}

main() {
    local name="${1:-}"

    require_database_container

    if [ -z "$name" ]; then
        name="$(latest_dump mybudget)"
    fi
    [ -n "$name" ] || fail "No backup found to verify."

    docker exec "$DB_CONTAINER" test -f "/backups/$name" \
        || fail "Backup $name does not exist in the volume."

    log "Verifying $name"

    # A half-written check database from an interrupted run would make the next
    # restore fail for the wrong reason, so it is always recreated.
    drop_check_database

    local output
    if ! docker exec "$DB_CONTAINER" createdb --username "$(postgres_owner)" "$CHECK_DB"; then
        fail "Could not create the $CHECK_DB database."
    fi

    if ! restore_into_check_db "$name"; then
        drop_check_database
        fail "Restoring $name into $CHECK_DB failed."
    fi

    if ! output=$(run_sanity_checks); then
        drop_check_database
        fail "Sanity checks failed on the restored database."
    fi

    drop_check_database

    log "Restore drill for $name passed:"
    while IFS= read -r line; do
        [ -n "$line" ] && log "  $line"
    done <<< "$output"
}

main "$@"
