#!/bin/bash
# Rebuild and redeploy the production website containers with two replicas.
# Pass --with-cli-assets when src/Automaton.Cli or shared/ai-guidance changed and the website should serve new CLI downloads.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/docker-compose.prod.yml"
WITH_CLI_ASSETS=false
REPLICA_COUNT="${AUTOMATON_DEPLOY_REPLICAS:-2}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --with-cli-assets)
            WITH_CLI_ASSETS=true
            shift
            ;;
        --help|-h)
            cat <<'EOF'
Usage: ./scripts/prod-redeploy-website.sh [--with-cli-assets]

Options:
  --with-cli-assets   Rebuild the downloadable CLI bundles before rebuilding the website. Use this when src/Automaton.Cli or shared/ai-guidance changed.
EOF
            exit 0
            ;;
        *)
            echo "[ERROR] Unknown argument: $1" >&2
            exit 1
            ;;
    esac
done

echo "[INFO] Redeploying production website from $REPO_ROOT with $REPLICA_COUNT replicas"

if [[ "$WITH_CLI_ASSETS" == "true" ]]; then
    echo "[INFO] Rebuilding website CLI assets image"
    docker compose -f "$COMPOSE_FILE" --profile build-assets build website-cli-assets
fi

docker compose -f "$COMPOSE_FILE" up -d --build --scale "website=$REPLICA_COUNT" website
