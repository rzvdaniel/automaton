#!/bin/bash
# Manage Traefik dynamic blue-green routing for api, studio, and website.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

print_usage() {
    cat <<'EOF'
Usage:
  ./scripts/prod-switch-blue-green.sh status
  ./scripts/prod-switch-blue-green.sh <api|studio|website> <blue|green>
  ./scripts/prod-switch-blue-green.sh all <blue|green>

Examples:
  ./scripts/prod-switch-blue-green.sh status
  ./scripts/prod-switch-blue-green.sh website green
  ./scripts/prod-switch-blue-green.sh all blue
EOF
}

if [[ $# -eq 0 ]]; then
    print_usage
    exit 1
fi

case "$1" in
    status)
        ensure_blue_green_bootstrap
        status_table
        ;;
    api|studio|website)
        if [[ $# -ne 2 ]]; then
            print_usage
            exit 1
        fi
        if [[ "$2" != "blue" && "$2" != "green" ]]; then
            print_usage
            exit 1
        fi
        ensure_infrastructure
        assert_service_exists "$1" "$2"
        switch_live_color "$1" "$2"
        echo "[INFO] Routed $1 traffic to $1-$2 via Traefik dynamic configuration"
        status_table
        ;;
    all)
        if [[ $# -ne 2 ]]; then
            print_usage
            exit 1
        fi
        if [[ "$2" != "blue" && "$2" != "green" ]]; then
            print_usage
            exit 1
        fi
        ensure_infrastructure
        assert_service_exists api "$2"
        assert_service_exists studio "$2"
        assert_service_exists website "$2"
        switch_live_color api "$2"
        switch_live_color studio "$2"
        switch_live_color website "$2"
        echo "[INFO] Routed api, studio, and website traffic to the $2 color via Traefik dynamic configuration"
        status_table
        ;;
    *)
        print_usage
        exit 1
        ;;
esac
