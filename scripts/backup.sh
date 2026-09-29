#!/usr/bin/env bash
#
# MyBudget-bot nightly backup.
#
#   ./scripts/backup.sh
#
# Dumps the live database into the mybudget_pg_backups volume, proves the dump is
# readable by pg_restore, promotes it into the weekly and monthly buckets, copies
# an age-encrypted version off-site when configured, and prunes the old ones.
#
# A dump that cannot be listed by pg_restore is treated as a failure, because a
# backup that has never been read is not a backup. The restore drill lives in
# scripts/verify-backup.sh and is what proves the dump can actually be loaded.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/backup-lib.sh
source "$SCRIPT_DIR/lib/backup-lib.sh"

LOG_FILE="${BACKUP_LOG_FILE:-/tmp/mybudget-backup.log}"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" | tee -a "$LOG_FILE"; }
fail() {
    log "BackupFailed $*"
    notify_admin "⚠️ El respaldo de MyBudget falló: $*"
    exit 1
}

daily_retention() {
    local value
    value="$(env_value BACKUP_RETENTION_DAYS || true)"
    echo "${value:-7}"
}

weekly_retention() {
    local value
    value="$(env_value BACKUP_WEEKLY_RETENTION || true)"
    echo "${value:-4}"
}

monthly_retention() {
    local value
    value="$(env_value BACKUP_MONTHLY_RETENTION || true)"
    echo "${value:-12}"
}

# Encrypts the dump with age before it leaves the machine. Optional: without a
# target or a recipient the nightly still runs locally and says so.
copy_offsite() {
    local name="$1"
    local target recipient

    target="$(env_value BACKUP_OFFSITE_TARGET || true)"
    if [ -z "$target" ]; then
        log "No off-site target configured; skipping the encrypted copy."
        return 0
    fi

    if ! command -v age >/dev/null 2>&1; then
        log "age is not installed; skipping the off-site copy."
        return 0
    fi

    recipient="$(env_value BACKUP_AGE_RECIPIENT || true)"
    [ -n "$recipient" ] || fail "BACKUP_OFFSITE_TARGET is set but BACKUP_AGE_RECIPIENT is missing."

    mkdir -p "$target"

    docker exec "$DB_CONTAINER" sh -c "cat '/backups/$name'" \
        | age --encrypt --recipient "$recipient" --output "$target/$name.age" \
        || fail "The encrypted off-site copy of $name failed."

    log "Encrypted off-site copy written to $target/$name.age"
}

main() {
    [ -f "$DEPLOY_DIR/.env" ] || fail ".env not found at $DEPLOY_DIR/.env"

    local name
    name="mybudget-$(date +%Y%m%d-%H%M%S).dump"

    log "Starting backup $name"
    dump_database "$name" >/dev/null
    verify_dump_readable "$name"
    log "Backup $name written and readable."

    # One long-lived copy per ISO week and per calendar month, taken from the
    # first successful backup of that period.
    promote_backup "$name" "weekly-$(date +%G-W%V).dump"
    promote_backup "$name" "monthly-$(date +%Y-%m).dump"

    copy_offsite "$name"

    prune_prefix "mybudget" "$(daily_retention)"
    prune_prefix "weekly" "$(weekly_retention)"
    prune_prefix "monthly" "$(monthly_retention)"

    log "Backup finished: $name"
}

main "$@"
