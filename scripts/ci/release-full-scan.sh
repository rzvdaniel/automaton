#!/usr/bin/env bash
# Weekly signed-in full scan of a throwaway install (OWASP ZAP automation plan scripts/ci/zap-full-plan.yaml): the AJAX
# spider crawls Studio signed in as the install's administrator, the API is imported from the OpenAPI file and both get ZAP's
# active scan. Active scans create junk data, so this runs only against a throwaway server, never a shared one or production.
#   release-full-scan.sh --api-spec openapi.json --api-key-file FILE --password-file FILE DOMAIN ADMIN_EMAIL
# Fails on high findings, missing security headers and stack traces (scan-report.py, scan-allowlist.txt are shared with the
# release scan), and when the crawl found no Studio page that needs a signed-in user. Failing findings go to $FAILING_JSON.
set -euo pipefail
api_spec=''; key_file=''; password_file=''
while [[ "${1:-}" == --* ]]; do
    case "$1" in
        --api-spec) api_spec="${2:?}"; shift 2 ;;
        --api-key-file) key_file="${2:?}"; shift 2 ;;
        --password-file) password_file="${2:?}"; shift 2 ;;
        *) echo "Unknown option $1"; exit 2 ;;
    esac
done
domain="${1:?Usage: release-full-scan.sh --api-spec F --api-key-file F --password-file F DOMAIN ADMIN_EMAIL}"; email="${2:?}"
[[ -s "$api_spec" && -s "$key_file" && -s "$password_file" ]] || { echo "::error::The OpenAPI file, API key file and password file are required"; exit 2; }
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
zap_image="${ZAP_IMAGE:-ghcr.io/zaproxy/zaproxy:stable}"
work=$(mktemp -d); chmod 777 "$work"
trap 'rm -rf "$work"' EXIT
key=$(tr -d '\r\n' < "$key_file"); password=$(tr -d '\r\n' < "$password_file")
[[ "$key" == ak_* ]] || { echo "::error::The API key file does not hold an API key"; exit 1; }
echo "::add-mask::$key"; echo "::add-mask::$password"

# Destructive endpoints never reach ZAP: out of the spec and out of every context (ZAP's exclusion alone does not stop the import's first call).
patterns=$(grep -vE '^[[:space:]]*(#|$)' "$here/api-scan-exclude.txt" | jq -R . | jq -s '. + [".*/logout.*"]')
jq --argjson rx "$patterns" '.paths |= with_entries(select(.key as $k | all($rx[]; . as $r | ($k | test("^" + $r + "$")) | not)))' "$api_spec" > "$work/openapi.json"
endpoints=$(jq '[.paths[] | keys[] | select(. == ("get","post","put","patch","delete","head","options"))] | length' "$work/openapi.json")
[[ "$endpoints" -gt 1 ]] || { echo "::error::The OpenAPI file lists $endpoints endpoints to scan; expected the whole API"; exit 1; }
DOMAIN="$domain" EXCLUDES="$patterns" python3 -I - "$here/zap-full-plan.yaml" "$work/plan.yaml" <<'PY'
import os, sys
text = open(sys.argv[1], encoding="utf-8").read()
d = os.environ["DOMAIN"]
text = text.replace("__DOMAIN_RX__", d.replace(".", "[.]")).replace("__DOMAIN__", d).replace("__EXCLUDES__", os.environ["EXCLUDES"].replace("\n", " "))
open(sys.argv[2], "w", encoding="utf-8").write(text)
PY

rc=0
# The credentials reach ZAP as environment variables, never in the plan file or its log.
docker run --rm -v "$work:/zap/wrk:rw" -e CI_ADMIN_EMAIL="$email" -e CI_ADMIN_PASSWORD="$password" -e CI_API_KEY="$key" \
    "$zap_image" zap.sh -cmd -autorun /zap/wrk/plan.yaml >"$work/zap.log" 2>&1 || rc=$?
sed -i -e "s/$key/***/g" -e "s/$password/***/g" "$work/zap.log" 2>/dev/null || true
[[ -s "$work/full.json" && -s "$work/sites.yaml" ]] || { tail -n 30 "$work/zap.log"; echo "::error::The ZAP plan produced no report (exit $rc)"; exit 1; }
[[ "$rc" == 0 ]] || { tail -n 30 "$work/zap.log"; echo "::error::The ZAP plan did not finish cleanly (exit $rc)"; exit 1; }

# What the crawl reached: Studio pages other than the public ones need the signed-in user.
mapfile -t routes < <(grep -oE "url: https://studio\.${domain//./\\.}[^ ]*" "$work/sites.yaml" | sed 's/^url: //' | sed -E 's#https://[^/]+##; s#[?#].*##' | sort -u)
signed_in=()
for r in "${routes[@]}"; do
    case "$r" in ''|/|/login|/terms|/privacy|/assets/*|/favicon*|/install*) ;; *) signed_in+=("$r") ;; esac
done
note="Full scan on $domain as $email: the AJAX spider found ${#routes[@]} Studio routes, ${#signed_in[@]} of them behind the sign-in; API: $endpoints endpoints from the OpenAPI file"
echo "$note"
[[ -z "${GITHUB_STEP_SUMMARY:-}" ]] || {
    printf -- '- %s\n' "$note" >> "$GITHUB_STEP_SUMMARY"
    printf -- '- Signed-in Studio routes: %s\n' "$(printf '`%s` ' "${signed_in[@]:0:20}")" >> "$GITHUB_STEP_SUMMARY"
}
[[ "${#signed_in[@]}" -gt 0 ]] || { echo "::error::The crawl found no Studio page behind the sign-in: the scan was not signed in"; exit 1; }

python3 -I "$here/scan-report.py" --allowlist "$here/scan-allowlist.txt" --zap "api-full=$work/full.json" \
    ${FAILING_JSON:+--failing-json "$FAILING_JSON"} ${GITHUB_STEP_SUMMARY:+--summary "$GITHUB_STEP_SUMMARY"}
