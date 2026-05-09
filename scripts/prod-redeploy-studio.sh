#!/bin/bash
# Rebuild and redeploy the production Studio containers with two replicas.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/docker-compose.prod.yml"
REPLICA_COUNT="${AUTOMATON_DEPLOY_REPLICAS:-2}"

echo "[INFO] Redeploying production Studio from $REPO_ROOT with $REPLICA_COUNT replicas"
docker compose -f "$COMPOSE_FILE" up -d --build --scale "studio=$REPLICA_COUNT" studio
