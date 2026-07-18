#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/../prod-blue-green-common.sh"

fallback_sleep_seconds=""

sleep() {
    fallback_sleep_seconds="$1"
}

wait_for_api_color_runner_drain() {
    return 2
}

drain_api_color_runners blue 60

if [[ "$fallback_sleep_seconds" != "60" ]]; then
    echo "Expected unsupported drain status to use the 60-second fallback, got '${fallback_sleep_seconds:-none}'." >&2
    exit 1
fi
