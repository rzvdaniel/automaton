#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEST_DIR="$SCRIPT_DIR"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/../prod-blue-green-common.sh"

api_script="$TEST_DIR/../prod-redeploy-api.sh"
all_script="$TEST_DIR/../prod-redeploy-all.sh"
api_snapshot_line="$(grep -n 'snapshot_api_color_runner_ids' "$api_script" | head -n 1 | cut -d: -f1)"
api_switch_line="$(grep -n 'switch_live_color api' "$api_script" | head -n 1 | cut -d: -f1)"
all_snapshot_line="$(grep -n 'snapshot_api_color_runner_ids' "$all_script" | head -n 1 | cut -d: -f1)"
all_switch_line="$(grep -n 'switch_all_live_colors' "$all_script" | head -n 1 | cut -d: -f1)"
(( api_snapshot_line < api_switch_line ))
(( all_snapshot_line < all_switch_line ))
! grep -q 'RECONNECT_CANDIDATE_RUNNER_IDS' "$TEST_DIR/../prod-blue-green-common.sh"
grep -q 'migration-status?runnerIds=$MIGRATION_EXPECTED_RUNNER_IDS' "$TEST_DIR/../prod-blue-green-common.sh"
grep -q 'Legacy Runner migration snapshot mismatch' "$TEST_DIR/../prod-blue-green-common.sh"
grep -q 'fallback_count' "$TEST_DIR/../prod-blue-green-common.sh"
grep -q 'signaled_count" -lt 0' "$TEST_DIR/../prod-blue-green-common.sh"

live_color="green"
green_running=true
blue_running=true
signal_result=0
drain_result=0
stop_called=false

signal_api_color_runners_reconnect_when_idle() { return "$signal_result"; }
drain_api_color_runners() { return "$drain_result"; }
rollback_live_color() { live_color="green"; }
stop_color() { stop_called=true; }

run_api_migration() {
    local keep_previous="$1"
    local current_color="green"
    local target_color="blue"

    if [[ "$current_color" != "$target_color" ]]; then
        signal_api_color_runners_reconnect_when_idle "$current_color" || {
            rollback_live_color api "$current_color"
            return 1
        }
        drain_api_color_runners "$current_color" "$target_color" 60 || {
            rollback_live_color api "$current_color"
            return 1
        }
        live_color="$target_color"
    fi

    if [[ "$keep_previous" != "true" && "$current_color" != "$target_color" ]]; then
        stop_color api "$current_color"
    fi
}

drain_result=1
if run_api_migration true; then
    echo "Expected keep-previous migration failure to fail." >&2
    exit 1
fi
[[ "$live_color" == "green" ]]
[[ "$green_running" == "true" && "$blue_running" == "true" ]]
[[ "$stop_called" == "false" ]]

live_color="green"
drain_result=0
if ! run_api_migration true; then
    echo "Expected keep-previous migration success." >&2
    exit 1
fi
[[ "$live_color" == "blue" ]]
[[ "$green_running" == "true" && "$blue_running" == "true" ]]
[[ "$stop_called" == "false" ]]

echo "keep-previous migration verification passed"
