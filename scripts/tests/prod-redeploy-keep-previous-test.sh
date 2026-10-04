#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
api_script="$SCRIPT_DIR/../prod-redeploy-api.sh"
all_script="$SCRIPT_DIR/../prod-redeploy-all.sh"
common_script="$SCRIPT_DIR/../prod-blue-green-common.sh"

if grep -Eq 'snapshot_api_color_runner_ids|signal_api_color_runners_reconnect_when_idle|drain_api_color_runners|internal/runners/(drain-status|migration-status|reconnect-when-idle)' \
    "$api_script" "$all_script" "$common_script"; then
    echo "API cutover must not wait for Runner registration or signal reconnect." >&2
    exit 1
fi

api_switch_line="$(grep -n 'switch_live_color api' "$api_script" | head -n 1 | cut -d: -f1)"
api_wait_line="$(grep -n 'wait_before_stopping_previous_color api' "$api_script" | head -n 1 | cut -d: -f1)"
api_stop_line="$(grep -n 'stop_color api' "$api_script" | head -n 1 | cut -d: -f1)"
all_switch_line="$(grep -n '^switch_all_live_colors ' "$all_script" | head -n 1 | cut -d: -f1)"
all_wait_line="$(grep -n 'wait_before_stopping_previous_color api' "$all_script" | head -n 1 | cut -d: -f1)"
all_stop_line="$(grep -n 'stop_color api' "$all_script" | head -n 1 | cut -d: -f1)"

[[ -n "$api_switch_line" && -n "$api_wait_line" && -n "$api_stop_line" ]]
[[ "$api_switch_line" -lt "$api_wait_line" && "$api_wait_line" -lt "$api_stop_line" ]]
[[ -n "$all_switch_line" && -n "$all_wait_line" && -n "$all_stop_line" ]]
[[ "$all_switch_line" -lt "$all_wait_line" && "$all_wait_line" -lt "$all_stop_line" ]]

echo "API slot cutover no longer gates on Runner reconnect; prior-color overlap remains before stop"
