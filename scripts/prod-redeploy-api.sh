#!/bin/bash
# Rebuild and redeploy the production API container.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/docker-compose.prod.yml"

echo "[INFO] Redeploying production API from $REPO_ROOT"
docker compose -f "$COMPOSE_FILE" up -d --build api
