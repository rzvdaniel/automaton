#!/bin/bash
# Verify the nginx production stack after bootstrap, deploy, rollback, or cert renewal.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

check() {
    local label="$1"
    shift

    echo "[INFO] $label"
    "$@"
}

check_url() {
    local label="$1"
    local url="$2"
    shift 2

    echo "[INFO] Checking $label: $url"
    curl -fsS "$@" "$url" > /dev/null
}

check_head() {
    local label="$1"
    local url="$2"
    shift 2

    echo "[INFO] Checking $label: $url"
    curl -fsSI "$@" "$url" > /dev/null
}

load_prod_env
load_state

echo "[INFO] Production blue/green state"
status_table

check "Docker Compose service status" docker compose -f "$COMPOSE_FILE" ps
check "nginx configuration" docker compose -f "$COMPOSE_FILE" exec -T nginx nginx -t

check_url "origin API health" "https://$API_DOMAIN/healthz" -k --resolve "$API_DOMAIN:443:127.0.0.1"
check_head "origin Studio" "https://$STUDIO_DOMAIN/" -k --resolve "$STUDIO_DOMAIN:443:127.0.0.1"
check_head "origin Website apex" "https://$WEBSITE_DOMAIN/" -k --resolve "$WEBSITE_DOMAIN:443:127.0.0.1"
check_head "origin Website www" "https://www.$WEBSITE_DOMAIN/" -k --resolve "www.$WEBSITE_DOMAIN:443:127.0.0.1"
check_head "origin Apps runtime" "https://$APPS_DOMAIN/runtime/automaton-api.js" -k --resolve "$APPS_DOMAIN:443:127.0.0.1"

check_url "public API health" "https://$API_DOMAIN/healthz"
check_head "public Studio" "https://$STUDIO_DOMAIN/"
check_head "public Website apex" "https://$WEBSITE_DOMAIN/"
check_head "public Website www" "https://www.$WEBSITE_DOMAIN/"
check_head "public Apps runtime" "https://$APPS_DOMAIN/runtime/automaton-api.js"

echo "[INFO] Production verification completed successfully"
