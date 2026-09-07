#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

common="$REPO_ROOT/scripts/prod-blue-green-common.sh"
studio="$REPO_ROOT/scripts/prod-redeploy-studio.sh"
website="$REPO_ROOT/scripts/prod-redeploy-website.sh"
all="$REPO_ROOT/scripts/prod-redeploy-all.sh"

# Recovery validation must be visible before infrastructure setup in each
# entrypoint; deploy_color remains only the defensive second layer.
studio_preflight="$(grep -n 'validate_recovery_deployment_options studio' "$studio" | head -n 1 | cut -d: -f1)"
studio_infra="$(grep -n '^ensure_infrastructure$' "$studio" | head -n 1 | cut -d: -f1)"
website_preflight="$(grep -n 'validate_recovery_deployment_options website' "$website" | head -n 1 | cut -d: -f1)"
website_infra="$(grep -n '^ensure_infrastructure$' "$website" | head -n 1 | cut -d: -f1)"
all_preflight="$(grep -n 'validate_recovery_deployment_options studio' "$all" | head -n 1 | cut -d: -f1)"
all_infra="$(grep -n '^ensure_infrastructure$' "$all" | head -n 1 | cut -d: -f1)"

(( studio_preflight < studio_infra ))
(( website_preflight < website_infra ))
(( all_preflight < all_infra ))
grep -q 'validate_recovery_deployment_options()' "$common"
grep -q 'local recovery_reference="\$2"' "$common"
grep -q 'local explicit_revision="\$3"' "$common"
grep -q 'validate_recovery_artifact_reference.*|| return 1' "$common"
grep -q 'local recovery_artifact_reference="\${5:-}"' "$common"

source "$common"
docker() { return 0; }
if validate_recovery_deployment_options studio automaton-prod-website:recovery true; then
    echo "Wrong-service recovery input must fail before mutation." >&2
    exit 1
fi
if validate_recovery_deployment_options studio automaton-prod-studio:recovery true; then
    echo "Revision plus recovery input must fail before mutation." >&2
    exit 1
fi

echo "recovery preflight verification passed"
