#!/usr/bin/env bash
# Rebuilds and restarts the marketing website (website-green) from an extracted `git archive` of the released commit.
# Runs on the production host as root. Never touches the /opt/automaton working checkout.
# Usage: deploy-website-prod.sh <archive-dir> <image-tag>
set -euo pipefail

ctx="${1:?archive directory required}"
tag="${2:?image tag required}"
env_file="${AUTOMATON_PRODUCTION_ENV_FILE:-/opt/automaton/.env}"
project="${AUTOMATON_PRODUCTION_PROJECT:-automaton-prod}"
compose_file="$ctx/docker-compose.prod.yml"

[[ "$tag" =~ ^[A-Za-z0-9_.-]{1,64}$ ]] || { echo "Invalid image tag." >&2; exit 1; }
[[ -f "$compose_file" && -f "$ctx/docker/Dockerfile.website" ]] || { echo "$ctx is not an Automaton source archive." >&2; exit 1; }
[[ -f "$env_file" ]] || { echo "Missing $env_file." >&2; exit 1; }

export AUTOMATON_PRODUCTION_BUILD_CONTEXT="$ctx"
export AUTOMATON_PRODUCTION_IMAGE_TAG="$tag"
compose=(docker compose -p "$project" --env-file "$env_file" -f "$compose_file")

"${compose[@]}" build website-green
"${compose[@]}" up -d --no-deps --no-build --wait website-green
echo "website-green is running automaton-prod-website:$tag and reports healthy."
