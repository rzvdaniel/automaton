#!/usr/bin/env bash
# Scans a throwaway install from outside: OWASP ZAP baseline (passive) against Studio, API, store and one app, nmap for
# open ports. Fails on high findings, missing security headers and unexpected ports; lists mediums in the run summary.
#   release-scan.sh DOMAIN IP APP_URL
#   release-scan.sh --zap-only DOMAIN WEBSITE_URL   read-only look at a live site: ZAP baseline against Studio, API health,
#                                                  store and the website; no nmap, no active scan
#   release-scan.sh --api-spec openapi.json --api-key-file FILE DOMAIN IP APP_URL
#                                                  also scans every endpoint of the OpenAPI file signed in with that API key
#                                                  (zap-api-scan.py, endpoints in api-scan-exclude.txt are skipped)
# Needs docker (and nmap, except with --zap-only) on the runner. Only the API scan is signed in (a CLI key of the throwaway server).
set -euo pipefail
zap_only=''; api_spec=''; api_key_file=''
while [[ "${1:-}" == --* ]]; do
    case "$1" in
        --zap-only) zap_only=1; shift ;;
        --api-spec) api_spec="${2:?}"; shift 2 ;;
        --api-key-file) api_key_file="${2:?}"; shift 2 ;;
        *) echo "Unknown option $1"; exit 2 ;;
    esac
done
[[ -z "$api_spec" ]] || [[ -s "$api_spec" && -s "$api_key_file" ]] || { echo "::error::--api-spec needs an OpenAPI file and --api-key-file a key file"; exit 2; }
domain="${1:?Usage: release-scan.sh [--zap-only] DOMAIN IP APP_URL}"; ip="${2:?}"; app_url="${3:-}"
if [[ -n "$zap_only" ]]; then app_url="$ip"; ip=''; fi  # --zap-only DOMAIN WEBSITE_URL
[[ -n "$app_url" ]] || { echo "Usage: release-scan.sh [--zap-only] DOMAIN IP APP_URL"; exit 2; }
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
zap_image="${ZAP_IMAGE:-ghcr.io/zaproxy/zaproxy:stable}"
work=$(mktemp -d); chmod 777 "$work"
trap 'rm -rf "$work"' EXIT

nmap_args=()
if [[ -z "$zap_only" ]]; then
    nmap -Pn -p- --open -T4 -oG "$work/nmap.gnmap" "$ip" >/dev/null
    nmap_args=(--nmap "$work/nmap.gnmap")
fi
zap_args=()
i=0
for target in "https://studio.$domain/" "https://api.$domain/health" "https://store.$domain/" "$app_url"; do
    i=$((i + 1))
    # Exit codes 1 and 2 only mean alerts were raised; the report decides. Anything else is a scan error.
    rc=0
    docker run --rm -v "$work:/zap/wrk:rw" "$zap_image" zap-baseline.py -t "$target" -J "z$i.json" -m 2 -I >"$work/z$i.log" 2>&1 || rc=$?
    [[ -s "$work/z$i.json" ]] || { tail -n 20 "$work/z$i.log"; echo "::error::ZAP produced no report for $target (exit $rc)"; exit 1; }
    zap_args+=(--zap "$target=$work/z$i.json")
done
if [[ -n "$api_spec" ]]; then
    # Destructive endpoints never reach ZAP: drop them from the spec (ZAP's own exclude list does not stop the import's first call).
    patterns=$(grep -vE '^[[:space:]]*(#|$)' "$here/api-scan-exclude.txt" | jq -R . | jq -s .)
    jq --argjson rx "$patterns" '.paths |= with_entries(select(.key as $k | all($rx[]; . as $r | ($k | test("^" + $r + "$")) | not)))' \
        "$api_spec" > "$work/openapi.json"
    endpoints=$(jq '[.paths[] | keys[] | select(. == ("get","post","put","patch","delete","head","options"))] | length' "$work/openapi.json")
    all_endpoints=$(jq '[.paths[] | keys[] | select(. == ("get","post","put","patch","delete","head","options"))] | length' "$api_spec")
    [[ "$endpoints" -gt 1 ]] || { echo "::error::The OpenAPI file lists $endpoints endpoints to scan; expected the whole API"; exit 1; }
    key=$(tr -d '\r\n' < "$api_key_file")
    [[ "$key" == ak_* ]] || { echo "::error::The API key file does not hold an API key"; exit 1; }
    echo "::add-mask::$key"
    zopts=(-config 'replacer.full_list(0).description=ci-auth' -config 'replacer.full_list(0).enabled=true'
        -config 'replacer.full_list(0).matchtype=REQ_HEADER' -config 'replacer.full_list(0).matchstr=Authorization'
        -config 'replacer.full_list(0).regex=false' -config "replacer.full_list(0).replacement=Bearer $key")
    n=0
    while IFS= read -r rx; do
        [[ -z "$rx" || "$rx" == \#* ]] && continue
        zopts+=(-config "globalexcludeurl.url_list.url($n).regex=$rx" -config "globalexcludeurl.url_list.url($n).enabled=true"); n=$((n + 1))
    done < "$here/api-scan-exclude.txt"
    rc=0
    # -T: at most 15 minutes of scanning; -I: alerts do not fail the script, the report decides.
    docker run --rm -v "$work:/zap/wrk:rw" "$zap_image" zap-api-scan.py -t /zap/wrk/openapi.json -f openapi -O "https://api.$domain" \
        -J zapi.json -T 15 -I -z "$(printf '%q ' "${zopts[@]}")" >"$work/zapi.log" 2>&1 || rc=$?
    [[ -s "$work/zapi.json" ]] || { sed "s/$key/***/g" "$work/zapi.log" | tail -n 20; echo "::error::ZAP API scan produced no report (exit $rc)"; exit 1; }
    zap_args+=(--zap "api-spec=$work/zapi.json")
    api_note="API scan: $endpoints of $all_endpoints endpoints in the OpenAPI file scanned signed in with a CLI key (the rest are destructive, see api-scan-exclude.txt)"
    echo "$api_note"
    [[ -z "${GITHUB_STEP_SUMMARY:-}" ]] || printf -- '- %s\n' "$api_note" >> "$GITHUB_STEP_SUMMARY"
fi
python3 -I "$here/scan-report.py" --allowlist "$here/scan-allowlist.txt" "${nmap_args[@]}" "${zap_args[@]}" \
    ${GITHUB_STEP_SUMMARY:+--summary "$GITHUB_STEP_SUMMARY"}
