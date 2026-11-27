#!/usr/bin/env bash
# Scans a throwaway install from outside: OWASP ZAP baseline (passive) against Studio, API, store and one app, nmap for
# open ports. Fails on high findings, missing security headers and unexpected ports; lists mediums in the run summary.
#   release-scan.sh DOMAIN IP APP_URL
#   release-scan.sh --zap-only DOMAIN WEBSITE_URL   read-only look at a live site: ZAP baseline against Studio, API health,
#                                                  store and the website; no nmap, no active scan
# Needs docker (and nmap, except with --zap-only) on the runner. No credentials are used (authenticated deep scans are out of scope).
set -euo pipefail
zap_only=''; [[ "${1:-}" == --zap-only ]] && { zap_only=1; shift; }
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
python3 -I "$here/scan-report.py" --allowlist "$here/scan-allowlist.txt" "${nmap_args[@]}" "${zap_args[@]}" \
    ${GITHUB_STEP_SUMMARY:+--summary "$GITHUB_STEP_SUMMARY"}
