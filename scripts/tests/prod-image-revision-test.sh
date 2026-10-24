#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

grep -q 'AUTOMATON_PRODUCTION_IMAGE_TAG:-latest' "$REPO_ROOT/docker-compose.prod.yml"
grep -q 'automaton-prod-api:<git-sha>' "$REPO_ROOT/.agents/skills/production-deployment/SKILL.md"
grep -q 'immediately' "$REPO_ROOT/.agents/skills/production-deployment/SKILL.md"
grep -q 'immediately previous immutable revision-tagged images' "$REPO_ROOT/.agents/skills/production-docker-maintenance/SKILL.md"

for script in prod-redeploy-api.sh prod-redeploy-studio.sh prod-redeploy-website.sh prod-redeploy-all.sh; do
    grep -q -- '--revision' "$REPO_ROOT/scripts/$script"
    grep -q 'preserve_current_service_image' "$REPO_ROOT/scripts/$script"
done

grep -q -- '--current-artifact-ref' "$REPO_ROOT/scripts/prod-redeploy-studio.sh"
grep -q -- '--current-artifact-ref' "$REPO_ROOT/scripts/prod-redeploy-website.sh"
grep -q -- '--studio-current-artifact-ref' "$REPO_ROOT/scripts/prod-redeploy-all.sh"
grep -q -- '--website-current-artifact-ref' "$REPO_ROOT/scripts/prod-redeploy-all.sh"

grep -q -- '--api' "$REPO_ROOT/scripts/prod-adopt-image-revisions.sh"
grep -q -- 'At least one service revision is required' "$REPO_ROOT/scripts/prod-adopt-image-revisions.sh"
grep -q -- 'change the database' "$REPO_ROOT/scripts/prod-adopt-image-revisions.sh"
grep -q 'docker tag' "$REPO_ROOT/scripts/prod-adopt-image-revisions.sh"
grep -q 'save_state' "$REPO_ROOT/scripts/prod-adopt-image-revisions.sh"

# The immutable-image path must skip a source build while still updating the
# convenience tag used by operators and local compose inspection.
source "$REPO_ROOT/scripts/prod-blue-green-common.sh"
PRODUCTION_REVISION="0123456789abcdef0123456789abcdef01234567"
CUTOVER_WARMUP_SECONDS=0
compose_calls=()
create_production_build_context() {
    echo "Source build context must not be created for immutable image reuse." >&2
    return 1
}
docker() {
    compose_calls+=("$*")
    if [[ "$*" == image\ inspect* ]]; then
        return 0
    fi
}
wait_for_service_healthy() { :; }
verify_target_from_traefik_network() { :; }
deploy_color api blue "$PRODUCTION_REVISION"

printf '%s\n' "${compose_calls[@]}" | grep -q -- 'compose.*up -d --no-build --no-deps api-blue'
printf '%s\n' "${compose_calls[@]}" | grep -q -- 'tag automaton-prod-api:0123456789abcdef0123456789abcdef01234567 automaton-prod-api:latest'
if printf '%s\n' "${compose_calls[@]}" | grep -q -- '--build'; then
    echo "Immutable image deployment must not build from source." >&2
    exit 1
fi

# A missing explicitly requested rollback image must fail before Compose is
# invoked, so a historical tag can never be assigned to a fresh build.
compose_calls=()
docker() {
    compose_calls+=("$*")
    if [[ "$*" == image\ inspect* ]]; then
        return 1
    fi
}
if deploy_color api blue "$PRODUCTION_REVISION" true; then
    echo "A missing explicit revision must fail closed." >&2
    exit 1
fi
if printf '%s\n' "${compose_calls[@]}" | grep -q 'compose'; then
    echo "A missing explicit revision must not invoke Compose." >&2
    exit 1
fi

# Unknown historical provenance may only be recovered with an explicitly
# supplied immutable reference whose image ID matches the running container.
state_file="$(mktemp)"
trap 'rm -f "$state_file"' EXIT
STATE_FILE="$state_file"
ensure_runtime_dirs() { :; }
cat > "$STATE_FILE" <<'EOF'
LIVE_API_COLOR=blue
LIVE_STUDIO_COLOR=blue
LIVE_WEBSITE_COLOR=blue
API_CURRENT_REVISION=
API_PREVIOUS_REVISION=
STUDIO_CURRENT_REVISION=
STUDIO_PREVIOUS_REVISION=
WEBSITE_CURRENT_REVISION=
WEBSITE_PREVIOUS_REVISION=
EOF
service_container_ids() { echo "studio-container"; }
docker() {
    if [[ "$1" == "compose" ]]; then
        compose_calls+=("$* env=${AUTOMATON_PRODUCTION_IMAGE_TAG:-}")
        return 0
    fi
    if [[ "$1" == "inspect" ]]; then
        echo "sha256:live-studio"
        return 0
    fi
    if [[ "$1" == "image" && "$2" == "inspect" ]]; then
        if [[ "${@: -1}" == "automaton-prod-studio:missing" ]]; then
            return 1
        fi
        if [[ "${@: -1}" == "automaton-prod-studio:recovery" ]]; then
            echo "sha256:live-studio"
            return 0
        fi
        echo "sha256:other"
        return 0
    fi
    return 0
}
if preserve_current_service_image studio blue; then
    echo "Unknown provenance must require an explicit recovery artifact." >&2
    exit 1
fi
preserve_current_service_image studio blue automaton-prod-studio:recovery
if preserve_current_service_image studio blue automaton-prod-studio:wrong; then
    echo "A mismatched recovery artifact must fail closed." >&2
    exit 1
fi
if validate_recovery_artifact_reference studio automaton-prod-studio:latest; then
    echo "latest must not be accepted as a recovery artifact." >&2
    exit 1
fi

# A recovery deployment must reuse the exact artifact without building.
compose_calls=()
deploy_color studio blue "$PRODUCTION_REVISION" false automaton-prod-studio:recovery
printf '%s\n' "${compose_calls[@]}" | grep -q -- 'compose.*up -d --no-build --no-deps studio-blue.*env=recovery' || {
    echo "Recovery deployment must use the dedicated recovery tag without building." >&2
    exit 1
}
if printf '%s\n' "${compose_calls[@]}" | grep -q -- '--build'; then
    echo "Recovery deployment must never build from source." >&2
    exit 1
fi
compose_calls=()
if deploy_color studio blue "$PRODUCTION_REVISION" false automaton-prod-studio:missing; then
    echo "A missing recovery artifact must fail closed." >&2
    exit 1
fi
if printf '%s\n' "${compose_calls[@]}" | grep -q 'compose'; then
    echo "A missing recovery artifact must not mutate Compose." >&2
    exit 1
fi

# Color and revision metadata must be written by the same successful switch.
STATE_FILE="$state_file"
cat > "$STATE_FILE" <<'EOF'
LIVE_API_COLOR=green
LIVE_STUDIO_COLOR=green
LIVE_WEBSITE_COLOR=green
API_CURRENT_REVISION=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
API_PREVIOUS_REVISION=9999999999999999999999999999999999999999
STUDIO_CURRENT_REVISION=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
STUDIO_PREVIOUS_REVISION=8888888888888888888888888888888888888888
WEBSITE_CURRENT_REVISION=cccccccccccccccccccccccccccccccccccccc
WEBSITE_PREVIOUS_REVISION=7777777777777777777777777777777777777777
EOF
render_service_upstream() { :; }
render_traefik_api_edge() { :; }
traefik_reload() { :; }
verify_live_origin() { :; }
soft_wait_for_public_origin() { :; }

# A recovery cutover clears current Git provenance while preserving the known
# revision being replaced as previous metadata.
switch_live_color studio blue "" aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa unknown
load_state
[[ "$LIVE_STUDIO_COLOR" == blue && -z "$STUDIO_CURRENT_REVISION" && "$STUDIO_PREVIOUS_REVISION" == aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ]]

switch_live_color api blue "$PRODUCTION_REVISION" "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
load_state
[[ "$LIVE_API_COLOR" == blue ]]
[[ "$API_CURRENT_REVISION" == "$PRODUCTION_REVISION" ]]
[[ "$API_PREVIOUS_REVISION" == aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ]]
rollback_live_color api green aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 9999999999999999999999999999999999999999
load_state
[[ "$LIVE_API_COLOR" == green ]]
[[ "$API_CURRENT_REVISION" == aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ]]
[[ "$API_PREVIOUS_REVISION" == 9999999999999999999999999999999999999999 ]]

switch_all_live_colors blue blue blue \
    "$PRODUCTION_REVISION" aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \
    "$PRODUCTION_REVISION" bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb \
    "$PRODUCTION_REVISION" cccccccccccccccccccccccccccccccccccccc
load_state
[[ "$LIVE_API_COLOR" == blue && "$LIVE_STUDIO_COLOR" == blue && "$LIVE_WEBSITE_COLOR" == blue ]]
[[ "$API_CURRENT_REVISION" == "$PRODUCTION_REVISION" && "$STUDIO_CURRENT_REVISION" == "$PRODUCTION_REVISION" && "$WEBSITE_CURRENT_REVISION" == "$PRODUCTION_REVISION" ]]
rollback_all_live_colors green green green \
    aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 9999999999999999999999999999999999999999 \
    bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 8888888888888888888888888888888888888888 \
    cccccccccccccccccccccccccccccccccccccc 7777777777777777777777777777777777777777
load_state
[[ "$LIVE_API_COLOR" == green && "$LIVE_STUDIO_COLOR" == green && "$LIVE_WEBSITE_COLOR" == green ]]
[[ "$API_CURRENT_REVISION" == aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa && "$STUDIO_CURRENT_REVISION" == bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb && "$WEBSITE_CURRENT_REVISION" == cccccccccccccccccccccccccccccccccccccc ]]

# Coordinated rollback must restore blank provenance for services that were
# historically unknown, rather than retaining the newly deployed revision.
cat > "$STATE_FILE" <<'EOF'
LIVE_API_COLOR=green
LIVE_STUDIO_COLOR=green
LIVE_WEBSITE_COLOR=green
API_CURRENT_REVISION=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
API_PREVIOUS_REVISION=9999999999999999999999999999999999999999
STUDIO_CURRENT_REVISION=
STUDIO_PREVIOUS_REVISION=
WEBSITE_CURRENT_REVISION=
WEBSITE_PREVIOUS_REVISION=
EOF
switch_all_live_colors blue blue blue \
    "$PRODUCTION_REVISION" aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \
    "$PRODUCTION_REVISION" "" \
    "$PRODUCTION_REVISION" "" \
    preserve preserve preserve
rollback_all_live_colors green green green \
    aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 9999999999999999999999999999999999999999 \
    "" "" \
    "" "" \
    preserve unknown unknown
load_state
[[ "$LIVE_API_COLOR" == green && "$LIVE_STUDIO_COLOR" == green && "$LIVE_WEBSITE_COLOR" == green ]]
[[ "$API_CURRENT_REVISION" == aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ]]
[[ -z "$STUDIO_CURRENT_REVISION" && -z "$WEBSITE_CURRENT_REVISION" ]]

echo "production image revision verification passed"
