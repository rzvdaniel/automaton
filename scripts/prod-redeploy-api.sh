#!/bin/bash
# Rebuild and redeploy the production API using a blue/green cutover.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

TARGET_COLOR=""
KEEP_PREVIOUS=false
DEPLOY_REVISION="$PRODUCTION_REVISION"
EXPLICIT_REVISION=false

print_usage() {
    cat <<'EOF'
Usage: ./scripts/prod-redeploy-api.sh [--color blue|green] [--keep-previous] [--revision git-sha]

Options:
  --color blue|green  Deploy to a specific color. Default: deploy to the inactive color.
  --keep-previous     Leave the previous color running after the cutover for easy rollback.
  --revision git-sha  Deploy an existing immutable image instead of building from source.
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
        --revision)
            DEPLOY_REVISION="${2:-}"
            EXPLICIT_REVISION=true
            shift 2
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

CURRENT_COLOR="$(get_live_color api)"
TARGET_COLOR="${TARGET_COLOR:-$(resolve_target_color api)}"
CURRENT_REVISION="$(get_service_revision api current)"
PREVIOUS_REVISION="$(get_service_revision api previous)"
preserve_current_service_image api "$CURRENT_COLOR"

echo "[INFO] Redeploying production API to $TARGET_COLOR (current live color: $CURRENT_COLOR)"
deploy_color api "$TARGET_COLOR" "$DEPLOY_REVISION" "$EXPLICIT_REVISION"

switch_live_color api "$TARGET_COLOR" "$DEPLOY_REVISION" "$CURRENT_REVISION"
echo "[INFO] Switched $API_DOMAIN traffic to api-$TARGET_COLOR via Traefik dynamic configuration"

if [[ "$CURRENT_COLOR" != "$TARGET_COLOR" ]] && ! request_api_color_runner_tunnel_reconnect "$TARGET_COLOR" "$TARGET_COLOR" false; then
    echo "[ERROR] Runtime tunnel reconnect requests failed; restoring API traffic to $CURRENT_COLOR" >&2
    if ! rollback_live_color api "$CURRENT_COLOR" "$CURRENT_REVISION" "$PREVIOUS_REVISION"; then
        echo "[ERROR] API route rollback failed; Runner tunnel reconciliation was not attempted" >&2
        exit 1
    fi
    if ! request_api_color_runner_tunnel_reconnect "$TARGET_COLOR" "$CURRENT_COLOR" true; then
        echo "[ERROR] API route was restored, but Runner tunnel rollback reconciliation failed; keep both API colors running and retry reconciliation" >&2
        exit 1
    fi
    exit 1
fi

if [[ "$KEEP_PREVIOUS" != "true" && "$CURRENT_COLOR" != "$TARGET_COLOR" ]]; then
    wait_before_stopping_previous_color api
    stop_color api "$CURRENT_COLOR"
fi

status_table
