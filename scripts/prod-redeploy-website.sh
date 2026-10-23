#!/bin/bash
# Rebuild and redeploy the production website using a blue/green cutover.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

TARGET_COLOR=""
KEEP_PREVIOUS=false
DEPLOY_REVISION="$PRODUCTION_REVISION"
EXPLICIT_REVISION=false
CURRENT_ARTIFACT_REFERENCE=""
RECOVERY_ARTIFACT_REFERENCE=""

print_usage() {
    cat <<'EOF'
Usage: ./scripts/prod-redeploy-website.sh [--color blue|green] [--keep-previous] [--revision git-sha] [--current-artifact-ref image-ref] [--recovery-artifact-ref image-ref]

Options:
  --color blue|green  Deploy to a specific color. Default: deploy to the inactive color.
  --keep-previous     Leave the previous color running after the cutover for easy rollback.
  --revision git-sha  Deploy an existing immutable image instead of building from source.
  --current-artifact-ref image-ref
                      Verify the currently running image when its Git revision is unknown.
  --recovery-artifact-ref image-ref
                      Deploy a previously verified immutable recovery artifact without building.
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
        --current-artifact-ref)
            CURRENT_ARTIFACT_REFERENCE="${2:-}"
            shift 2
            ;;
        --recovery-artifact-ref)
            RECOVERY_ARTIFACT_REFERENCE="${2:-}"
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

validate_recovery_deployment_options website "$RECOVERY_ARTIFACT_REFERENCE" "$EXPLICIT_REVISION"

CURRENT_COLOR="$(get_live_color website)"
TARGET_COLOR="${TARGET_COLOR:-$(resolve_target_color website)}"
CURRENT_REVISION="$(get_service_revision website current)"
if [[ -z "$CURRENT_REVISION" ]]; then
    preserve_current_service_image website "$CURRENT_COLOR" "$CURRENT_ARTIFACT_REFERENCE"
fi

ensure_infrastructure

if [[ -n "$CURRENT_REVISION" ]]; then
    preserve_current_service_image website "$CURRENT_COLOR"
fi

echo "[INFO] Redeploying production website to $TARGET_COLOR (current live color: $CURRENT_COLOR)"
deploy_color website "$TARGET_COLOR" "$DEPLOY_REVISION" "$EXPLICIT_REVISION" "$RECOVERY_ARTIFACT_REFERENCE"
if [[ -n "$RECOVERY_ARTIFACT_REFERENCE" ]]; then
    switch_live_color website "$TARGET_COLOR" "" "$CURRENT_REVISION" unknown
else
    switch_live_color website "$TARGET_COLOR" "$DEPLOY_REVISION" "$CURRENT_REVISION"
fi
echo "[INFO] Switched $WEBSITE_DOMAIN and www.$WEBSITE_DOMAIN traffic to website-$TARGET_COLOR via Traefik dynamic configuration"

if [[ "$KEEP_PREVIOUS" != "true" && "$CURRENT_COLOR" != "$TARGET_COLOR" ]]; then
    wait_before_stopping_previous_color website
    stop_color website "$CURRENT_COLOR"
fi

status_table
maintain_deployment_images
