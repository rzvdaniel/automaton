#!/bin/bash
# Rebuild and redeploy the production worker service.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

print_usage() {
    cat <<'EOF'
Usage: ./scripts/prod-redeploy-worker.sh

Rebuilds and restarts the private production worker container after ensuring
shared infrastructure is healthy.
EOF
}

if [[ $# -gt 0 ]]; then
    case "$1" in
        --help|-h)
            print_usage
            exit 0
            ;;
        *)
            echo "[ERROR] Unknown argument: $1" >&2
            print_usage
            exit 1
            ;;
    esac
fi

ensure_infrastructure

echo "[INFO] Rebuilding and redeploying production worker"
docker compose -f "$COMPOSE_FILE" up -d --build --remove-orphans worker
wait_for_service_healthy worker 120

echo "[INFO] Production worker is running"
docker compose -f "$COMPOSE_FILE" ps worker
