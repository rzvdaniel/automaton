#!/bin/bash
# Rebuild and redeploy the production website using a blue/green cutover.
# Pass --with-cli-assets when src/Automaton.Cli or shared/ai-guidance changed and the website should serve new CLI downloads.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

TARGET_COLOR=""
KEEP_PREVIOUS=false
WITH_CLI_ASSETS=false

print_usage() {
    cat <<'EOF'
Usage: ./scripts/prod-redeploy-website.sh [--with-cli-assets] [--color blue|green] [--keep-previous]

Options:
  --with-cli-assets   Rebuild the downloadable CLI bundles before rebuilding the website. Use this when src/Automaton.Cli or shared/ai-guidance changed.
  --color blue|green  Deploy to a specific color. Default: deploy to the inactive color.
  --keep-previous     Leave the previous color running after the cutover for easy rollback.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --with-cli-assets)
            WITH_CLI_ASSETS=true
            shift
            ;;
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

if [[ "$WITH_CLI_ASSETS" == "true" ]]; then
    echo "[INFO] Rebuilding website CLI assets image"
    docker compose -f "$COMPOSE_FILE" --profile build-assets build website-cli-assets
fi

CURRENT_COLOR="$(get_live_color website)"
TARGET_COLOR="${TARGET_COLOR:-$(resolve_target_color website)}"

echo "[INFO] Redeploying production website to $TARGET_COLOR (current live color: $CURRENT_COLOR)"
deploy_color website "$TARGET_COLOR"
switch_live_color website "$TARGET_COLOR"
echo "[INFO] Switched $WEBSITE_DOMAIN and www.$WEBSITE_DOMAIN traffic to website-$TARGET_COLOR via nginx reload"

if [[ "$KEEP_PREVIOUS" != "true" && "$CURRENT_COLOR" != "$TARGET_COLOR" ]]; then
    stop_color website "$CURRENT_COLOR"
fi

status_table
