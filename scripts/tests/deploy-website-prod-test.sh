#!/usr/bin/env bash
# Runs deploy-website-prod.sh with a fake docker and checks the exact compose calls. Touches nothing real.
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/bin" "$tmp/ctx/docker"
touch "$tmp/ctx/docker-compose.prod.yml" "$tmp/ctx/docker/Dockerfile.website" "$tmp/env"
cat > "$tmp/bin/docker" <<'FAKE'
#!/usr/bin/env bash
echo "docker $* | ctx=$AUTOMATON_PRODUCTION_BUILD_CONTEXT tag=$AUTOMATON_PRODUCTION_IMAGE_TAG" >> "$FAKE_LOG"
FAKE
chmod +x "$tmp/bin/docker"
export FAKE_LOG="$tmp/log" PATH="$tmp/bin:$PATH" AUTOMATON_PRODUCTION_ENV_FILE="$tmp/env"
bash "$repo/scripts/deploy-website-prod.sh" "$tmp/ctx" 0.2.170
cat "$FAKE_LOG"
base="docker compose -p automaton-prod --env-file $tmp/env -f $tmp/ctx/docker-compose.prod.yml"
suffix="| ctx=$tmp/ctx tag=0.2.170"
[[ "$(sed -n 1p "$FAKE_LOG")" == "$base build website-green $suffix" ]]
[[ "$(sed -n 2p "$FAKE_LOG")" == "$base up -d --no-deps --no-build --wait website-green $suffix" ]]
[[ "$(wc -l < "$FAKE_LOG")" -eq 2 ]]
if bash "$repo/scripts/deploy-website-prod.sh" "$tmp/ctx" 'bad tag' 2>/dev/null; then echo "bad tag accepted"; exit 1; fi
if bash "$repo/scripts/deploy-website-prod.sh" "$tmp/nope" 1.0.0 2>/dev/null; then echo "bad dir accepted"; exit 1; fi
echo PASS
