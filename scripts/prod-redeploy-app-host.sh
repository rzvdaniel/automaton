#!/bin/bash
# Refresh nginx config and the static runtime SDK for apps.automaton.monster.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

ensure_infrastructure

echo "[INFO] Reloading nginx app host for $APPS_DOMAIN"
nginx_reload
verify_app_host_origin

echo "[INFO] App host redeploy completed"
