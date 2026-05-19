#!/bin/bash
# Rebuild and redeploy the production API using a blue/green cutover.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

TARGET_COLOR=""
KEEP_PREVIOUS=false

print_usage() {
    cat <<'EOF'
Usage: ./scripts/prod-redeploy-api.sh [--color blue|green] [--keep-previous]

Options:
  --color blue|green  Deploy to a specific color. Default: deploy to the inactive color.
  --keep-previous     Leave the previous color running after the cutover for easy rollback.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --color)
            TARGET_COLOR="${2:-}"
            shift 2
            ;;
        --keep-previous)
            KEEP_PREVIOUS=true
            shift
            ;;
        --help|-h)
            print_usage
            exit 0
            ;;
        *)
            echo "[ERROR] Unknown argument: $1" >&2
            print_usage
            exit 1
            ;;
    esac
done

if [[ -n "$TARGET_COLOR" && "$TARGET_COLOR" != "blue" && "$TARGET_COLOR" != "green" ]]; then
    echo "[ERROR] --color must be blue or green" >&2
    exit 1
fi

ensure_infrastructure
DRAIN_WAIT_SECONDS="$RUNNER_RECONNECT_GRACE_SECONDS"

CURRENT_COLOR="$(get_live_color api)"
TARGET_COLOR="${TARGET_COLOR:-$(resolve_target_color api)}"

echo "[INFO] Redeploying production API to $TARGET_COLOR (current live color: $CURRENT_COLOR)"
deploy_color api "$TARGET_COLOR"
switch_live_color api "$TARGET_COLOR"
echo "[INFO] Switched $API_DOMAIN traffic to api-$TARGET_COLOR via nginx reload"

if [[ "$CURRENT_COLOR" != "$TARGET_COLOR" ]]; then
    signal_api_color_runners_reconnect_when_idle "$CURRENT_COLOR"
fi

if [[ "$KEEP_PREVIOUS" != "true" && "$CURRENT_COLOR" != "$TARGET_COLOR" ]]; then
    echo "[INFO] Waiting ${DRAIN_WAIT_SECONDS}s for runners on api-$CURRENT_COLOR to reconnect"
    sleep "$DRAIN_WAIT_SECONDS"
    stop_color api "$CURRENT_COLOR"
fi

status_table
