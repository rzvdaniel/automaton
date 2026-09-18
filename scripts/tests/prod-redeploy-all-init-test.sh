#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TEST_ROOT="$(mktemp -d)"
trap 'rm -rf -- "$TEST_ROOT"' EXIT

cp "$REPO_ROOT/scripts/prod-redeploy-all.sh" "$TEST_ROOT/prod-redeploy-all.sh"

ensure_line="$(grep -n '^ensure_infrastructure$' "$REPO_ROOT/scripts/prod-redeploy-all.sh" | cut -d: -f1)"
drain_wait_line="$(grep -n '^DRAIN_WAIT_SECONDS=' "$REPO_ROOT/scripts/prod-redeploy-all.sh" | cut -d: -f1)"
[[ -n "$ensure_line" && -n "$drain_wait_line" && "$ensure_line" -lt "$drain_wait_line" ]]

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
    record "ensure:$RUNNER_RECONNECT_GRACE_SECONDS"
}

deploy_color() { record "deploy:$1:$2"; }
snapshot_api_color_runner_ids() { record "snapshot:$1:$2"; }
switch_all_live_colors() { record "switch:$1:$2:$3"; }
signal_api_color_runners_reconnect_when_idle() { record "signal:$1"; }
drain_api_color_runners() { record "drain:$3"; }
wait_before_stopping_previous_color() { record "wait:$1"; }
stop_color() { record "stop:$1:$2"; }
status_table() { record 'status'; }
EOF

write_env() {
    local path="$1"
    local reconnect_grace="$2"

    cat > "$path" <<'EOF'
API_DOMAIN=api.example.invalid
STUDIO_DOMAIN=studio.example.invalid
WEBSITE_DOMAIN=example.invalid
APPS_DOMAIN=apps.example.invalid
ACME_EMAIL=ops@example.invalid
RUNTIME_PROXY_AUTH_KEY=test-only-placeholder
EOF

    if [[ "$reconnect_grace" != "unset" ]]; then
        printf 'RUNNER_RECONNECT_GRACE_SECONDS=%s\n' "$reconnect_grace" >> "$path"
    fi
}

run_case() {
    local name="$1"
    local configured_grace="$2"
    local expected_grace="$3"
    local keep_previous="$4"
    local expected_stops="$5"
    local env_file="$TEST_ROOT/$name.env"
    local events="$TEST_ROOT/$name.events"
    local -a args=()

    write_env "$env_file" "$configured_grace"
    : > "$events"

    if [[ "$keep_previous" == "true" ]]; then
        args+=(--keep-previous)
    fi

    env -u RUNNER_RECONNECT_GRACE_SECONDS \
        TEST_COMMON_SCRIPT="$REPO_ROOT/scripts/prod-blue-green-common.sh" \
        TEST_ENV_FILE="$env_file" \
        TEST_EVENTS="$events" \
        bash "$TEST_ROOT/prod-redeploy-all.sh" "${args[@]}"

    grep -Fxq "ensure:$expected_grace" "$events"
    grep -Fxq "drain:$expected_grace" "$events"
    grep -Fxq 'switch:green:blue:green' "$events"

    if [[ "$expected_stops" == "none" ]]; then
        if grep -q '^stop:' "$events"; then
            echo "$name unexpectedly stopped a previous service with --keep-previous." >&2
            exit 1
        fi
    else
        [[ "$(grep -c '^stop:' "$events")" -eq 3 ]]
    fi
}

# The production example config specifies the reconnect grace value; loading
# it must happen before strict-mode code reads the value.
documented_grace="$(grep '^RUNNER_RECONNECT_GRACE_SECONDS=' "$REPO_ROOT/.env.prod.example" | cut -d= -f2)"
[[ -n "$documented_grace" ]]
run_case documented-config "$documented_grace" "$documented_grace" true none

# If the optional setting is absent, common production defaults resolve to 60.
run_case common-default unset 60 true none

# An explicit environment value is preserved, and no flag still stops the old
# colors after a successful cutover.
run_case explicit-config 37 37 false all

echo "production full-stack initialization and keep-previous checks passed"
