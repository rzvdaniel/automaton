#!/bin/bash
# Rebuild and redeploy the public-facing production application stack with blue/green cutovers.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

KEEP_PREVIOUS=false
API_COLOR=""
STUDIO_COLOR=""
WEBSITE_COLOR=""
DEPLOY_REVISION="$PRODUCTION_REVISION"
EXPLICIT_REVISION=false
STUDIO_CURRENT_ARTIFACT_REFERENCE=""
WEBSITE_CURRENT_ARTIFACT_REFERENCE=""
STUDIO_RECOVERY_ARTIFACT_REFERENCE=""
WEBSITE_RECOVERY_ARTIFACT_REFERENCE=""

print_usage() {
    cat <<'EOF'
Usage: ./scripts/prod-redeploy-all.sh [--keep-previous] [--revision git-sha] [--api-color blue|green] [--studio-color blue|green] [--website-color blue|green] [--studio-current-artifact-ref image-ref] [--website-current-artifact-ref image-ref] [--studio-recovery-artifact-ref image-ref] [--website-recovery-artifact-ref image-ref]

Options:
  --keep-previous         Leave the previous color running after the cutover for easy rollback.
  --api-color blue|green      Force the API deploy target color.
  --studio-color blue|green   Force the Studio deploy target color.
  --website-color blue|green  Force the website deploy target color.
  --revision git-sha       Deploy an existing immutable image instead of building from source.
  --studio-current-artifact-ref image-ref
                           Verify the live Studio image when its Git revision is unknown.
  --website-current-artifact-ref image-ref
                           Verify the live Website image when its Git revision is unknown.
  --studio-recovery-artifact-ref image-ref
                           Deploy a verified Studio recovery artifact without building.
  --website-recovery-artifact-ref image-ref
                           Deploy a verified Website recovery artifact without building.
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
        --revision)
            DEPLOY_REVISION="${2:-}"
            EXPLICIT_REVISION=true
            shift 2
            ;;
        --studio-current-artifact-ref)
            STUDIO_CURRENT_ARTIFACT_REFERENCE="${2:-}"
            shift 2
            ;;
        --website-current-artifact-ref)
            WEBSITE_CURRENT_ARTIFACT_REFERENCE="${2:-}"
            shift 2
            ;;
        --studio-recovery-artifact-ref)
            STUDIO_RECOVERY_ARTIFACT_REFERENCE="${2:-}"
            shift 2
            ;;
        --website-recovery-artifact-ref)
            WEBSITE_RECOVERY_ARTIFACT_REFERENCE="${2:-}"
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
validate_recovery_deployment_options studio "$STUDIO_RECOVERY_ARTIFACT_REFERENCE" "$EXPLICIT_REVISION"
validate_recovery_deployment_options website "$WEBSITE_RECOVERY_ARTIFACT_REFERENCE" "$EXPLICIT_REVISION"

CURRENT_API_COLOR="$(get_live_color api)"
CURRENT_STUDIO_COLOR="$(get_live_color studio)"
CURRENT_WEBSITE_COLOR="$(get_live_color website)"
CURRENT_API_REVISION="$(get_service_revision api current)"
CURRENT_API_PREVIOUS_REVISION="$(get_service_revision api previous)"
CURRENT_STUDIO_REVISION="$(get_service_revision studio current)"
CURRENT_STUDIO_PREVIOUS_REVISION="$(get_service_revision studio previous)"
CURRENT_WEBSITE_REVISION="$(get_service_revision website current)"
CURRENT_WEBSITE_PREVIOUS_REVISION="$(get_service_revision website previous)"
CURRENT_API_REVISION_STATE="preserve"
CURRENT_STUDIO_REVISION_STATE="preserve"
CURRENT_WEBSITE_REVISION_STATE="preserve"
[[ -n "$CURRENT_API_REVISION" ]] || CURRENT_API_REVISION_STATE="unknown"
[[ -n "$CURRENT_STUDIO_REVISION" ]] || CURRENT_STUDIO_REVISION_STATE="unknown"
[[ -n "$CURRENT_WEBSITE_REVISION" ]] || CURRENT_WEBSITE_REVISION_STATE="unknown"

TARGET_API_COLOR="${API_COLOR:-$(resolve_target_color api)}"
TARGET_STUDIO_COLOR="${STUDIO_COLOR:-$(resolve_target_color studio)}"
TARGET_WEBSITE_COLOR="${WEBSITE_COLOR:-$(resolve_target_color website)}"

if [[ -z "$CURRENT_API_REVISION" ]]; then
    preserve_current_service_image api "$CURRENT_API_COLOR"
fi
if [[ -z "$CURRENT_STUDIO_REVISION" ]]; then
    preserve_current_service_image studio "$CURRENT_STUDIO_COLOR" "$STUDIO_CURRENT_ARTIFACT_REFERENCE"
fi
if [[ -z "$CURRENT_WEBSITE_REVISION" ]]; then
    preserve_current_service_image website "$CURRENT_WEBSITE_COLOR" "$WEBSITE_CURRENT_ARTIFACT_REFERENCE"
fi

ensure_infrastructure

if [[ -n "$CURRENT_API_REVISION" ]]; then
    preserve_current_service_image api "$CURRENT_API_COLOR"
fi
if [[ -n "$CURRENT_STUDIO_REVISION" ]]; then
    preserve_current_service_image studio "$CURRENT_STUDIO_COLOR"
fi
if [[ -n "$CURRENT_WEBSITE_REVISION" ]]; then
    preserve_current_service_image website "$CURRENT_WEBSITE_COLOR"
fi

echo "[INFO] Redeploying API to $TARGET_API_COLOR (current live color: $CURRENT_API_COLOR)"
deploy_color api "$TARGET_API_COLOR" "$DEPLOY_REVISION" "$EXPLICIT_REVISION"

echo "[INFO] Redeploying Studio to $TARGET_STUDIO_COLOR (current live color: $CURRENT_STUDIO_COLOR)"
deploy_color studio "$TARGET_STUDIO_COLOR" "$DEPLOY_REVISION" "$EXPLICIT_REVISION" "$STUDIO_RECOVERY_ARTIFACT_REFERENCE"

echo "[INFO] Redeploying website to $TARGET_WEBSITE_COLOR (current live color: $CURRENT_WEBSITE_COLOR)"
deploy_color website "$TARGET_WEBSITE_COLOR" "$DEPLOY_REVISION" "$EXPLICIT_REVISION" "$WEBSITE_RECOVERY_ARTIFACT_REFERENCE"

STUDIO_SWITCH_REVISION="$DEPLOY_REVISION"
WEBSITE_SWITCH_REVISION="$DEPLOY_REVISION"
if [[ -n "$STUDIO_RECOVERY_ARTIFACT_REFERENCE" ]]; then
    STUDIO_SWITCH_REVISION=""
fi
if [[ -n "$WEBSITE_RECOVERY_ARTIFACT_REFERENCE" ]]; then
    WEBSITE_SWITCH_REVISION=""
fi

switch_all_live_colors "$TARGET_API_COLOR" "$TARGET_STUDIO_COLOR" "$TARGET_WEBSITE_COLOR" \
    "$DEPLOY_REVISION" "$CURRENT_API_REVISION" \
    "$STUDIO_SWITCH_REVISION" "$CURRENT_STUDIO_REVISION" \
    "$WEBSITE_SWITCH_REVISION" "$CURRENT_WEBSITE_REVISION" \
    preserve \
    "$(if [[ -n "$STUDIO_RECOVERY_ARTIFACT_REFERENCE" ]]; then echo unknown; else echo preserve; fi)" \
    "$(if [[ -n "$WEBSITE_RECOVERY_ARTIFACT_REFERENCE" ]]; then echo unknown; else echo preserve; fi)"

echo "[INFO] Traefik is now routing api, studio, and website to the freshly deployed colors"

if [[ "$CURRENT_API_COLOR" != "$TARGET_API_COLOR" ]] && ! request_api_color_runner_tunnel_reconnect "$TARGET_API_COLOR" "$TARGET_API_COLOR" false; then
    echo "[ERROR] Runtime tunnel reconnect requests failed; restoring previous public colors" >&2
    if ! rollback_all_live_colors "$CURRENT_API_COLOR" "$CURRENT_STUDIO_COLOR" "$CURRENT_WEBSITE_COLOR" \
        "$CURRENT_API_REVISION" "$CURRENT_API_PREVIOUS_REVISION" \
        "$CURRENT_STUDIO_REVISION" "$CURRENT_STUDIO_PREVIOUS_REVISION" \
        "$CURRENT_WEBSITE_REVISION" "$CURRENT_WEBSITE_PREVIOUS_REVISION" \
        "$CURRENT_API_REVISION_STATE" "$CURRENT_STUDIO_REVISION_STATE" "$CURRENT_WEBSITE_REVISION_STATE"; then
        echo "[ERROR] Public route rollback failed; Runner tunnel reconciliation was not attempted" >&2
        exit 1
    fi
    if ! request_api_color_runner_tunnel_reconnect "$TARGET_API_COLOR" "$CURRENT_API_COLOR" true; then
        echo "[ERROR] Public routes were restored, but Runner tunnel rollback reconciliation failed; keep both API colors running and retry reconciliation" >&2
        exit 1
    fi
    exit 1
fi

if [[ "$KEEP_PREVIOUS" != "true" ]]; then
    if [[ "$CURRENT_API_COLOR" != "$TARGET_API_COLOR" ]]; then
        wait_before_stopping_previous_color api
        stop_color api "$CURRENT_API_COLOR"
    fi
    if [[ "$CURRENT_STUDIO_COLOR" != "$TARGET_STUDIO_COLOR" ]]; then
        wait_before_stopping_previous_color studio
        stop_color studio "$CURRENT_STUDIO_COLOR"
    fi
    if [[ "$CURRENT_WEBSITE_COLOR" != "$TARGET_WEBSITE_COLOR" ]]; then
        wait_before_stopping_previous_color website
        stop_color website "$CURRENT_WEBSITE_COLOR"
    fi
fi

status_table
maintain_deployment_images
