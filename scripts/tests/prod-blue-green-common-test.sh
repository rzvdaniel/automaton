#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/../prod-blue-green-common.sh"

template="$REPO_ROOT/docker/traefik/prod/api-edge.yml"
grep -q 'api-live-edge:' "$template"
grep -q 'url: "http://api-__API_COLOR__:8080"' "$template"
grep -q '^tcp:' "$template"
grep -q 'rule: "HostSNI(`__API_DOMAIN__`)"' "$template"
grep -q 'address: "rabbitmq:5672"' "$template"
grep -q 'runner-broker:' "$REPO_ROOT/docker/traefik/prod/traefik.yml"
grep -q 'HostedRunnerRabbitMq__TlsEnabled: "true"' "$REPO_ROOT/docker-compose.prod.yml"
if grep -q 'api-blue-edge' "$template"; then
    echo "The production API edge template must not hardcode the blue runtime service." >&2
    exit 1
fi

common_script="$REPO_ROOT/scripts/prod-blue-green-common.sh"
grep -q 'all_drained.*==.*true.*migration_complete.*==.*true' "$common_script" || {
    echo "Runner drain must not succeed before migration completes." >&2
    exit 1
}

fallback_sleep_seconds=""

sleep() {
    fallback_sleep_seconds="$1"
}

wait_for_api_color_runner_drain() {
    return 2
}

if drain_api_color_runners blue green 60; then
    echo "Expected unsupported drain status to fail the deployment." >&2
    exit 1
fi

if [[ -n "$fallback_sleep_seconds" ]]; then
    echo "Unsupported drain status must not sleep and continue deployment." >&2
    exit 1
fi

live_color="blue"
blue_running=true
switch_live_color() {
    live_color="$2"
}

verify_live_origin() {
    [[ "$1" == "api" && "$live_color" == "green" ]]
}

if ! rollback_live_color api green; then
    echo "Expected API rollback to verify successfully." >&2
    exit 1
fi

if [[ "$live_color" != "green" ]]; then
    echo "Expected failed migration rollback to restore green as Live." >&2
    exit 1
fi

if [[ "$blue_running" != "true" ]]; then
    echo "Expected failed migration rollback to preserve blue for diagnosis." >&2
    exit 1
fi

declare -A live_colors=(
    [api]=blue
    [studio]=blue
    [website]=blue
)

switch_all_live_colors() {
    live_colors[api]="$1"
    live_colors[studio]="$2"
    live_colors[website]="$3"
}

verify_live_origin() {
    [[ "${live_colors[$1]}" == "green" ]]
}

if ! rollback_all_live_colors green green green; then
    echo "Expected coordinated rollback to verify successfully." >&2
    exit 1
fi

for service in api studio website; do
    if [[ "${live_colors[$service]}" != "green" ]]; then
        echo "Expected $service to roll back to green." >&2
        exit 1
    fi
done
