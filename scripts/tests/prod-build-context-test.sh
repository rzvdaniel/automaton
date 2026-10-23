#!/bin/bash

set -euo pipefail

TEST_SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$TEST_SCRIPT_DIR/../.." && pwd)"
source "$REPO_ROOT/scripts/prod-blue-green-common.sh"
storage_check() { :; } # Capacity decisions are covered by host-storage-test.sh.
storage_require_local_docker() { :; }

TEST_REPO="$(mktemp -d)"
trap 'rm -rf -- "$TEST_REPO"' EXIT

git -C "$TEST_REPO" init -q
git -C "$TEST_REPO" config user.email test@example.invalid
git -C "$TEST_REPO" config user.name "Build Context Test"
printf 'value=A\n' > "$TEST_REPO/tracked.txt"
git -C "$TEST_REPO" add tracked.txt
git -C "$TEST_REPO" commit -q -m "revision A"
REVISION_A="$(git -C "$TEST_REPO" rev-parse HEAD)"

printf 'value=B\n' > "$TEST_REPO/tracked.txt"
printf 'must not enter context\n' > "$TEST_REPO/untracked-file.txt"
mkdir -p "$TEST_REPO/backups" "$TEST_REPO/secrets" "$TEST_REPO/venv"
printf 'protected\n' > "$TEST_REPO/backups/protected.txt"
printf 'protected\n' > "$TEST_REPO/secrets/protected.txt"
printf 'protected\n' > "$TEST_REPO/venv/protected.txt"
printf 'protected\n' > "$TEST_REPO/privatekey.xml"
printf 'protected\n' > "$TEST_REPO/publickey.xml"

REPO_ROOT="$TEST_REPO"
context="$(create_production_build_context "$REVISION_A")"
grep -Fxq 'value=A' "$context/tracked.txt"
! grep -Fq 'value=B' "$context/tracked.txt"
! test -e "$context/untracked-file.txt"
! test -e "$context/backups"
! test -e "$context/secrets"
! test -e "$context/venv"
! test -e "$context/privatekey.xml"
! test -e "$context/publickey.xml"
rm -rf -- "$context"

# Source builds receive the temporary tracked snapshot and clean it on success.
COMPOSE_FILE="$TEST_REPO/docker-compose.prod.yml"
BUILD_LOG="$TEST_REPO/build.log"
docker() {
    if [[ "$*" == 'buildx version' ]]; then return; fi
    if [[ "$*" == 'buildx inspect --format {{.Driver}}' ]]; then echo docker; return; fi
    printf '%s|%s\n' "$*" "${AUTOMATON_PRODUCTION_BUILD_CONTEXT:-}" >> "$BUILD_LOG"
    [[ -f "${AUTOMATON_PRODUCTION_BUILD_CONTEXT:-}/tracked.txt" ]]
}
run_source_build api blue "$REVISION_A"
build_context="$(cut -d'|' -f2 "$BUILD_LOG")"
[[ -n "$build_context" ]]
! test -e "$build_context"

# The standalone worker rebuild uses the same tracked snapshot boundary.
if grep -Fq 'docker compose -f "$COMPOSE_FILE" up -d --build --remove-orphans worker' "$TEST_SCRIPT_DIR/../prod-redeploy-worker.sh"; then
    echo "Worker rebuild must not bypass the tracked production build context." >&2
    exit 1
fi
grep -Fq 'run_source_build_service worker "$PRODUCTION_REVISION" --remove-orphans' "$TEST_SCRIPT_DIR/../prod-redeploy-worker.sh"

# A failed source build also cleans the temporary context.
: > "$BUILD_LOG"
docker() {
    if [[ "$*" == 'buildx version' ]]; then return; fi
    if [[ "$*" == 'buildx inspect --format {{.Driver}}' ]]; then echo docker; return; fi
    printf '%s|%s\n' "$*" "${AUTOMATON_PRODUCTION_BUILD_CONTEXT:-}" >> "$BUILD_LOG"
    return 42
}
if run_source_build api blue "$REVISION_A"; then
    echo "Expected the mocked source build to fail." >&2
    exit 1
fi
build_context="$(cut -d'|' -f2 "$BUILD_LOG")"
[[ -n "$build_context" ]]
! test -e "$build_context"

# The default remains usable for local Compose workflows.
compose_context_count="$(grep -Fc 'context: ${AUTOMATON_PRODUCTION_BUILD_CONTEXT:-.}' "$TEST_SCRIPT_DIR/../../docker-compose.prod.yml")"
[[ "$compose_context_count" -eq 4 ]]

echo "production tracked build-context verification passed"
