#!/usr/bin/env bash
#
# Shared helpers for the MyBudget-bot operational scripts.
#
# Sourced, never executed. The caller owns `set -euo pipefail`, `log` and `fail`,
# so deploy.sh can keep its own logging while reusing the database primitives.
#
# Everything here works against the running database container and the backup
# volume mounted at /backups, so no dump ever has to travel through a host path.

DEPLOY_DIR="${DEPLOY_DIR:-/opt/mybudget}"
DB_CONTAINER="${DB_CONTAINER:-mybudget-db}"
BACKUP_VOLUME="${BACKUP_VOLUME:-mybudget_pg_backups}"

# Reads a value from .env without executing it.
env_value() {
    grep -E "^$1=" "$DEPLOY_DIR/.env" | head -n 1 | cut -d= -f2-
}

postgres_owner() { env_value POSTGRES_USER; }

postgres_database() { env_value POSTGRES_DB; }

database_is_running() {
    docker ps --format '{{.Names}}' | grep -qx "$DB_CONTAINER"
}

require_database_container() {
    database_is_running || fail "Database container $DB_CONTAINER is not running."
}

# Dumps the live database into the backup volume and echoes the file name.
dump_database() {
    local name="$1"
    require_database_container

    docker exec "$DB_CONTAINER" \
        pg_dump --format=custom --no-owner --no-privileges \
        --username "$(postgres_owner)" \
        --dbname "$(postgres_database)" \
        --file="/backups/$name" \
        || fail "pg_dump failed."

    echo "$name"
}

# Fails unless pg_restore can read the dump's table of contents. This catches a
# truncated or corrupted file, which is the common way a losing backup looks fine.
verify_dump_readable() {
    local name="$1"

    docker exec "$DB_CONTAINER" pg_restore --list "/backups/$name" >/dev/null 2>&1 \
        || fail "Backup $name is not readable by pg_restore."
}

# Dump file names in the volume matching a prefix, newest first.
list_dumps() {
    local prefix="$1"

    docker exec "$DB_CONTAINER" sh -c "ls -1 /backups/${prefix}-*.dump 2>/dev/null || true" \
        | sed 's#.*/##' | sort -r
}

# The newest dump for a prefix, or nothing.
latest_dump() {
    list_dumps "${1:-mybudget}" | head -n 1
}

# Hardlinks a dump into a long-lived bucket (week or month) if that bucket does
# not exist yet. Hardlinks share the inode, so the bucket survives pruning the
# daily file it came from while costing no extra space.
promote_backup() {
    local name="$1"
    local link="$2"

    docker exec "$DB_CONTAINER" sh -c \
        "[ -e '/backups/$link' ] || ln '/backups/$name' '/backups/$link'" \
        || fail "Could not promote $name to $link."
}

# Keeps the newest $2 dumps for a prefix and deletes the rest.
prune_prefix() {
    local prefix="$1"
    local keep="$2"
    local index=0
    local name

    while IFS= read -r name; do
        [ -n "$name" ] || continue
        index=$((index + 1))

        if [ "$index" -gt "$keep" ]; then
            docker exec "$DB_CONTAINER" rm -f "/backups/$name" || true
            log "Pruned backup $name"
        fi
    done < <(list_dumps "$prefix")
}

# Sends one Telegram alert to the admin user.
#
# Deliberately a direct API call rather than a trip through the application: an
# alert matters most when the application or database is unhealthy. It never
# fails the caller, because an alerting problem must not mask the incident.
notify_admin() {
    local text="$1"
    local token admin

    token="$(env_value TELEGRAM_BOT_TOKEN || true)"
    admin="$(env_value ADMIN_TELEGRAM_USER_ID || true)"

    if [ -z "$token" ] || [ -z "$admin" ]; then
        log "Admin alert not sent: TELEGRAM_BOT_TOKEN or ADMIN_TELEGRAM_USER_ID is missing."
        return 0
    fi

    if curl --fail --silent --show-error --max-time 15 \
        "https://api.telegram.org/bot${token}/sendMessage" \
        --data-urlencode "chat_id=${admin}" \
        --data-urlencode "text=${text}" >/dev/null; then
        log "Admin alert sent."
    else
        log "Admin alert could not be delivered."
    fi
}
