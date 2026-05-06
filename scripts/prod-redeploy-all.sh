#!/bin/bash
# Rebuild and redeploy the production application stack.
# Pass --with-cli-assets when src/Automaton.Cli changed and the website should serve new CLI downloads.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/docker-compose.prod.yml"
WITH_CLI_ASSETS=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --with-cli-assets)
            WITH_CLI_ASSETS=true
            shift
            ;;
        --help|-h)
            cat <<'EOF'
Usage: ./scripts/prod-redeploy-all.sh [--with-cli-assets]

Options:
  --with-cli-assets   Rebuild the downloadable CLI bundles before rebuilding the website.
EOF
            exit 0
            ;;
        *)
            echo "[ERROR] Unknown argument: $1" >&2
            exit 1
            ;;
    esac
done

echo "[INFO] Redeploying full production stack from $REPO_ROOT"

if [[ "$WITH_CLI_ASSETS" == "true" ]]; then
    echo "[INFO] Rebuilding website CLI assets image"
    docker compose -f "$COMPOSE_FILE" --profile build-assets build website-cli-assets
fi

docker compose -f "$COMPOSE_FILE" up -d --build
