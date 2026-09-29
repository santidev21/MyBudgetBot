#!/usr/bin/env bash
#
# Installs the nightly backup and weekly restore-drill cron entries for the user
# running this script. Idempotent: the managed block is replaced, never doubled.
#
#   ./scripts/install-backup-cron.sh
#
# The schedule is deliberately off the hour so it does not collide with other
# jobs on the host. See docs/BACKUPS.md.
#
set -euo pipefail

DEPLOY_DIR="${DEPLOY_DIR:-/opt/mybudget}"
BEGIN_MARKER="# BEGIN mybudget backups (managed)"
END_MARKER="# END mybudget backups"

BACKUP_SCHEDULE="${BACKUP_SCHEDULE:-15 3 * * *}"
VERIFY_SCHEDULE="${VERIFY_SCHEDULE:-15 4 * * 0}"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"; }
fail() { log "ERROR: $*"; exit 1; }

command -v crontab >/dev/null 2>&1 || fail "crontab is not installed."

[ -x "$DEPLOY_DIR/scripts/backup.sh" ] || fail "$DEPLOY_DIR/scripts/backup.sh is missing or not executable."
[ -x "$DEPLOY_DIR/scripts/verify-backup.sh" ] || fail "$DEPLOY_DIR/scripts/verify-backup.sh is missing or not executable."

LOG_DIR="$DEPLOY_DIR/logs"
mkdir -p "$LOG_DIR"

block="$(
    cat <<EOF
$BEGIN_MARKER
$BACKUP_SCHEDULE $DEPLOY_DIR/scripts/backup.sh >> $LOG_DIR/backup.log 2>&1
$VERIFY_SCHEDULE $DEPLOY_DIR/scripts/verify-backup.sh >> $LOG_DIR/verify.log 2>&1
$END_MARKER
EOF
)"

existing="$(crontab -l 2>/dev/null || true)"
cleaned="$(printf '%s\n' "$existing" | sed "/^# BEGIN mybudget backups/,/^# END mybudget backups/d")"

printf '%s\n%s\n' "$cleaned" "$block" | crontab -

log "Installed backup cron entries:"
printf '%s\n' "$block"
