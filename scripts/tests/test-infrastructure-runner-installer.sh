#!/usr/bin/env bash
set -euo pipefail

# Fast regression coverage for safety properties that must remain true even
# when Docker is unavailable in CI. End-to-end host tests cover Docker itself.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
INSTALLER="$ROOT/scripts/install-infrastructure-runner.sh"

fail() { echo "FAIL: $*" >&2; exit 1; }
require() { grep -Fq -- "$1" "$INSTALLER" || fail "missing: $1"; }

bash -n "$INSTALLER"

# One lifecycle operation per host, including update/repair/replace.
require 'flock -n 9'
require 'Another Infrastructure Runner lifecycle operation is already running'

# A repair never falls back to a mutable tag: it gets the existing immutable
# container image unless an explicit version was supplied.
require 'if [[ "$OPERATION" == repair && "$IMAGE_WAS_EXPLICIT" == false ]]'
require 'repair requires --version when no existing Infrastructure Runner container is available'

# Replacement validates the target before destroying the durable identity.
target_line="$(grep -n 'resolve_image$' "$INSTALLER" | tail -n 1 | cut -d: -f1)"
identity_line="$(grep -n 'installation-id' "$INSTALLER" | tail -n 1 | cut -d: -f1)"
[[ "$target_line" -lt "$identity_line" ]] || fail 'replacement identity is removed before image resolution'

# Update success requires both local health and the actual connected log; do
# not accept broad, ambiguous text such as "Runner connection".
require 'curl -fsS http://127.0.0.1:8001/health'
require 'Successfully connected to server'
! grep -Fq 'Runner connection' "$INSTALLER" || fail 'ambiguous connection log match remains'

# Rollback has an independently verified outcome.
require 'Update failed; rollback succeeded.'
require 'Update failed; rollback also failed. Manual recovery is required.'

# All successful paths preserve the canonical non-secret configuration.
require 'container-specification.env'
require 'save_installation_configuration'

echo 'Infrastructure Runner installer safety checks passed.'
