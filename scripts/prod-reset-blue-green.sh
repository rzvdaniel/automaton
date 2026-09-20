#!/bin/bash
# Reset production routing state to all-blue after those containers are already running.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

ensure_blue_green_bootstrap

assert_service_exists api blue
assert_service_exists studio blue
assert_service_exists website blue

reset_blue_green_to_blue

echo "[INFO] Reset blue/green routing state to blue for api, studio, and website"
status_table
