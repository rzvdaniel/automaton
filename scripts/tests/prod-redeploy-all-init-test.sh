#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TEST_ROOT="$(mktemp -d)"
trap 'rm -rf -- "$TEST_ROOT"' EXIT

cp "$REPO_ROOT/scripts/prod-redeploy-all.sh" "$TEST_ROOT/prod-redeploy-all.sh"

! rg -q 'RunnerDrain|reconnect-when-idle|drain-status|migration-status|DRAIN_WAIT_SECONDS' "$REPO_ROOT/scripts/prod-redeploy-all.sh"

cat > "$TEST_ROOT/prod-blue-green-common.sh" <<'EOF'
set -euo pipefail
source "$TEST_COMMON_SCRIPT"
ENV_FILE="$TEST_ENV_FILE"

record() {
    printf '%s\n' "$*" >> "$TEST_EVENTS"
}

validate_recovery_deployment_options() { :; }

get_live_color() {
    case "$1" in
        api|website) printf 'blue\n' ;;
        studio) printf 'green\n' ;;
        *) return 1 ;;
    esac
}

get_service_revision() { printf 'revision-%s-%s\n' "$1" "$2"; }

resolve_target_color() {
    case "$1" in
        api|website) printf 'green\n' ;;
        studio) printf 'blue\n' ;;
        *) return 1 ;;
    esac
}

preserve_current_service_image() { record "preserve:$1:$2"; }

ensure_infrastructure() {
    load_prod_env
    record 'ensure:loaded'
}

deploy_color() { record "deploy:$1:$2"; }
switch_all_live_colors() { record "switch:$1:$2:$3"; }
request_api_color_runner_tunnel_reconnect() { record "tunnel-reconnect:$1"; }
wait_before_stopping_previous_color() { record "wait:$1"; }
stop_color() { record "stop:$1:$2"; }
status_table() { record 'status'; }
EOF

write_env() {
    local path="$1"
    cat > "$path" <<'EOF'
API_DOMAIN=api.example.invalid
STUDIO_DOMAIN=studio.example.invalid
WEBSITE_DOMAIN=example.invalid
APPS_DOMAIN=apps.example.invalid
ACME_EMAIL=ops@example.invalid
RUNTIME_PROXY_AUTH_KEY=test-only-placeholder
EOF
}

run_case() {
    local name="$1"
    local keep_previous="$2"
    local expected_stops="$3"
    local env_file="$TEST_ROOT/$name.env"
    local events="$TEST_ROOT/$name.events"
    local -a args=()

    write_env "$env_file"
    : > "$events"

    if [[ "$keep_previous" == "true" ]]; then
        args+=(--keep-previous)
    fi

    env TEST_COMMON_SCRIPT="$REPO_ROOT/scripts/prod-blue-green-common.sh" \
        TEST_ENV_FILE="$env_file" \
        TEST_EVENTS="$events" \
        bash "$TEST_ROOT/prod-redeploy-all.sh" "${args[@]}"

    grep -Fxq 'ensure:loaded' "$events"
    grep -Fxq 'switch:green:blue:green' "$events"
    grep -Fxq 'tunnel-reconnect:green' "$events"
    switch_line="$(grep -n '^switch:' "$events" | cut -d: -f1)"
    reconnect_line="$(grep -n '^tunnel-reconnect:' "$events" | cut -d: -f1)"
    [[ "$switch_line" -lt "$reconnect_line" ]]

    if [[ "$expected_stops" == "none" ]]; then
        if grep -q '^stop:' "$events"; then
            echo "$name unexpectedly stopped a previous service with --keep-previous." >&2
            exit 1
        fi
    else
        [[ "$(grep -c '^stop:' "$events")" -eq 3 ]]
        wait_line="$(grep -n '^wait:api$' "$events" | cut -d: -f1)"
        [[ "$reconnect_line" -lt "$wait_line" ]]
    fi
}

run_case keep-previous true none
run_case stop-previous false all

echo "production full-stack initialization and keep-previous checks passed"
