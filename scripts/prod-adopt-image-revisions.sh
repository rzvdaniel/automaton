#!/bin/bash
# Adopt the currently running production images under known Git revisions.
# This is a metadata/tagging operation only; it does not build or restart.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

API_REVISION=""
STUDIO_REVISION=""
WEBSITE_REVISION=""

print_usage() {
    cat <<'EOF'
Usage: ./scripts/prod-adopt-image-revisions.sh [--api git-sha] [--studio git-sha] [--website git-sha]

Adopt currently running live-color images under explicitly supplied immutable
Git revision tags. At least one service revision is required. This command does
not build, deploy, restart, switch colors, or change the database.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --api)
            API_REVISION="${2:-}"
            shift 2
            ;;
        --studio)
            STUDIO_REVISION="${2:-}"
            shift 2
            ;;
        --website)
            WEBSITE_REVISION="${2:-}"
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

declare -a SERVICES=()
declare -a REVISIONS=()
declare -a IMAGE_IDS=()

add_service() {
    local service="$1"
    local revision="$2"
    local color
    color="$(get_live_color "$service")"

    validate_revision "$revision"

    local compose_service
    compose_service="$(compose_service_name "$service" "$color")"
    mapfile -t container_ids < <(service_container_ids "$compose_service")
    if [[ ${#container_ids[@]} -ne 1 || -z "${container_ids[0]:-}" ]]; then
        echo "[ERROR] Cannot adopt $service: expected exactly one running $compose_service container." >&2
        return 1
    fi

    local image_id
    image_id="$(docker inspect --format '{{.Image}}' "${container_ids[0]}")"
    if [[ -z "$image_id" ]] || ! docker image inspect "$image_id" >/dev/null 2>&1; then
        echo "[ERROR] Cannot verify the running image for $compose_service; no state was changed." >&2
        return 1
    fi

    SERVICES+=("$service")
    REVISIONS+=("$revision")
    IMAGE_IDS+=("$image_id")
}

if [[ -z "$API_REVISION$STUDIO_REVISION$WEBSITE_REVISION" ]]; then
    echo "[ERROR] Supply an explicit --api, --studio, or --website revision." >&2
    exit 1
fi

# Validate and inspect every requested service before creating any tags or
# changing state, so a bad service does not leave partial metadata behind.
[[ -z "$API_REVISION" ]] || add_service api "$API_REVISION"
[[ -z "$STUDIO_REVISION" ]] || add_service studio "$STUDIO_REVISION"
[[ -z "$WEBSITE_REVISION" ]] || add_service website "$WEBSITE_REVISION"

for i in "${!SERVICES[@]}"; do
    image_name="$(service_image_name "${SERVICES[$i]}")"
    docker tag "${IMAGE_IDS[$i]}" "$image_name:${REVISIONS[$i]}"
    echo "[INFO] Adopted ${SERVICES[$i]} live image as $image_name:${REVISIONS[$i]}"
done

load_state
for i in "${!SERVICES[@]}"; do
    service="${SERVICES[$i]}"
    revision="${REVISIONS[$i]}"
    case "$service" in
        api) API_CURRENT_REVISION="$revision" ;;
        studio) STUDIO_CURRENT_REVISION="$revision" ;;
        website) WEBSITE_CURRENT_REVISION="$revision" ;;
    esac
done
save_state

echo "[INFO] Adopted ${#SERVICES[@]} live production image revision(s); no deployment performed"
