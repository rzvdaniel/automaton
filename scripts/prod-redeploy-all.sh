#!/bin/bash
# Rebuild and redeploy the public-facing production application stack with blue/green cutovers.
# Pass --with-cli-assets when src/Automaton.Cli or shared/ai-guidance changed and the website should serve new CLI downloads.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

WITH_CLI_ASSETS=false
KEEP_PREVIOUS=false
API_COLOR=""
STUDIO_COLOR=""
WEBSITE_COLOR=""

print_usage() {
    cat <<'EOF'
Usage: ./scripts/prod-redeploy-all.sh [--with-cli-assets] [--keep-previous] [--api-color blue|green] [--studio-color blue|green] [--website-color blue|green]

Options:
  --with-cli-assets       Rebuild the downloadable CLI bundles before rebuilding the website. Use this when src/Automaton.Cli or shared/ai-guidance changed.
  --keep-previous         Leave the previous color running after the cutover for easy rollback.
  --api-color blue|green      Force the API deploy target color.
  --studio-color blue|green   Force the Studio deploy target color.
  --website-color blue|green  Force the website deploy target color.
EOF
}

validate_color() {
    local value="$1"
    local label="$2"
    if [[ -n "$value" && "$value" != "blue" && "$value" != "green" ]]; then
        echo "[ERROR] $label must be blue or green" >&2
        exit 1
    fi
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --with-cli-assets)
            WITH_CLI_ASSETS=true
            shift
            ;;
        --keep-previous)
            KEEP_PREVIOUS=true
            shift
            ;;
        --api-color)
            API_COLOR="${2:-}"
            shift 2
            ;;
        --studio-color)
            STUDIO_COLOR="${2:-}"
            shift 2
            ;;
        --website-color)
            WEBSITE_COLOR="${2:-}"
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

validate_color "$API_COLOR" "--api-color"
validate_color "$STUDIO_COLOR" "--studio-color"
validate_color "$WEBSITE_COLOR" "--website-color"

ensure_infrastructure

if [[ "$WITH_CLI_ASSETS" == "true" ]]; then
    echo "[INFO] Rebuilding website CLI assets image"
    docker compose -f "$COMPOSE_FILE" --profile build-assets build website-cli-assets
fi

CURRENT_API_COLOR="$(get_live_color api)"
CURRENT_STUDIO_COLOR="$(get_live_color studio)"
CURRENT_WEBSITE_COLOR="$(get_live_color website)"

TARGET_API_COLOR="${API_COLOR:-$(resolve_target_color api)}"
TARGET_STUDIO_COLOR="${STUDIO_COLOR:-$(resolve_target_color studio)}"
TARGET_WEBSITE_COLOR="${WEBSITE_COLOR:-$(resolve_target_color website)}"

echo "[INFO] Redeploying API to $TARGET_API_COLOR (current live color: $CURRENT_API_COLOR)"
deploy_color api "$TARGET_API_COLOR"

echo "[INFO] Redeploying Studio to $TARGET_STUDIO_COLOR (current live color: $CURRENT_STUDIO_COLOR)"
deploy_color studio "$TARGET_STUDIO_COLOR"

echo "[INFO] Redeploying website to $TARGET_WEBSITE_COLOR (current live color: $CURRENT_WEBSITE_COLOR)"
deploy_color website "$TARGET_WEBSITE_COLOR"

switch_live_color api "$TARGET_API_COLOR"
switch_live_color studio "$TARGET_STUDIO_COLOR"
switch_live_color website "$TARGET_WEBSITE_COLOR"

echo "[INFO] nginx is now routing api, studio, and website to the freshly deployed colors"

if [[ "$KEEP_PREVIOUS" != "true" ]]; then
    if [[ "$CURRENT_API_COLOR" != "$TARGET_API_COLOR" ]]; then
        stop_color api "$CURRENT_API_COLOR"
    fi
    if [[ "$CURRENT_STUDIO_COLOR" != "$TARGET_STUDIO_COLOR" ]]; then
        stop_color studio "$CURRENT_STUDIO_COLOR"
    fi
    if [[ "$CURRENT_WEBSITE_COLOR" != "$TARGET_WEBSITE_COLOR" ]]; then
        stop_color website "$CURRENT_WEBSITE_COLOR"
    fi
fi

status_table
