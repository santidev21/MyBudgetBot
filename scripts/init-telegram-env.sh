#!/usr/bin/env bash
#
# Fills the Telegram settings in .env.
#
#   ./scripts/init-telegram-env.sh                      fill what can be generated, report the rest
#   ./scripts/init-telegram-env.sh --token <token> --user-id <id>
#   ./scripts/init-telegram-env.sh --polling            local chat development
#   ./scripts/init-telegram-env.sh --rotate             regenerate the two local secrets
#
# Only two of the four values come from Telegram:
#   TELEGRAM_BOT_TOKEN           from @BotFather
#   ALLOWED_TELEGRAM_USER_IDS    your own id, from @userinfobot
# The other two are random values this script generates; Telegram only ever echoes them back.
#
# Secret values are never printed.
#
set -euo pipefail

ENV_FILE="${ENV_FILE:-.env}"
ROTATE=false
TOKEN=""
USER_ID=""
POLLING=""

# Values that appear in .env.example purely as illustrations and must be replaced.
EXAMPLE_PLACEHOLDERS="123456789 123456789:AAExampleBotTokenIssuedByBotFather"

while [ $# -gt 0 ]; do
    case "$1" in
        --token) TOKEN="${2:-}"; shift 2 ;;
        --user-id) USER_ID="${2:-}"; shift 2 ;;
        --polling) POLLING=true; shift ;;
        --webhook) POLLING=false; shift ;;
        --rotate) ROTATE=true; shift ;;
        -h|--help) sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done

[ -f "$ENV_FILE" ] || { echo "ERROR: $ENV_FILE not found. Copy .env.example to .env first." >&2; exit 1; }
command -v openssl >/dev/null || { echo "ERROR: openssl is required to generate secrets." >&2; exit 1; }

value_of() { grep -E "^$1=" "$ENV_FILE" | head -n 1 | cut -d= -f2-; }

set_value() {
    local key="$1" value="$2"
    if grep -qE "^${key}=" "$ENV_FILE"; then
        # Only the matching line is rewritten, so values containing '=' are left untouched.
        awk -v k="$key" -v v="$value" '$0 ~ "^" k "=" { print k "=" v; next } { print }' \
            "$ENV_FILE" > "${ENV_FILE}.tmp"
        mv "${ENV_FILE}.tmp" "$ENV_FILE"
    else
        printf '%s=%s\n' "$key" "$value" >> "$ENV_FILE"
    fi
}

needs_filling() {
    local current
    current="$(value_of "$1")"

    # Empty, the CHANGE_ME markers, or the example values shipped in .env.example.
    [ -z "$current" ] && return 0
    [ "${current#CHANGE_ME}" != "$current" ] && return 0
    case " ${EXAMPLE_PLACEHOLDERS} " in *" ${current} "*) return 0 ;; esac

    return 1
}

# --- values this script owns ------------------------------------------------
if [ "$ROTATE" = true ] || needs_filling TELEGRAM_WEBHOOK_SECRET; then
    set_value TELEGRAM_WEBHOOK_SECRET "$(openssl rand -hex 32)"
    echo "generated  TELEGRAM_WEBHOOK_SECRET"
fi

if [ "$ROTATE" = true ] || needs_filling TELEGRAM_WEBHOOK_PATH; then
    set_value TELEGRAM_WEBHOOK_PATH "$(openssl rand -hex 24)"
    echo "generated  TELEGRAM_WEBHOOK_PATH"
fi

# --- values only you have ---------------------------------------------------
[ -n "$TOKEN" ] && { set_value TELEGRAM_BOT_TOKEN "$TOKEN"; echo "set        TELEGRAM_BOT_TOKEN"; }
[ -n "$USER_ID" ] && { set_value ALLOWED_TELEGRAM_USER_IDS "$USER_ID"; echo "set        ALLOWED_TELEGRAM_USER_IDS"; }
[ -n "$POLLING" ] && { set_value TELEGRAM_USE_POLLING "$POLLING"; echo "set        TELEGRAM_USE_POLLING=$POLLING"; }

# --- report -----------------------------------------------------------------
missing=()
needs_filling TELEGRAM_BOT_TOKEN && missing+=("TELEGRAM_BOT_TOKEN          from @BotFather (/newbot)")
needs_filling ALLOWED_TELEGRAM_USER_IDS && missing+=("ALLOWED_TELEGRAM_USER_IDS   your id, from @userinfobot")

if [ ${#missing[@]} -gt 0 ]; then
    echo
    echo "Still missing, and only you can provide them:"
    printf '  - %s\n' "${missing[@]}"
    echo
    echo "Re-run with:  ./scripts/init-telegram-env.sh --token <token> --user-id <id> --polling"
    exit 1
fi

echo
echo "Telegram settings are complete."
[ "$(value_of TELEGRAM_USE_POLLING)" = "true" ] \
    && echo "Polling is on: delete any registered webhook first (--delete-webhook)."
