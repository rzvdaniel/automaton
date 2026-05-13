#!/bin/bash
# Request or refresh the production TLS certificate bundle used by nginx.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

FORCE_RENEWAL=false

print_usage() {
    cat <<'EOF'
Usage: ./scripts/prod-issue-certs.sh [--force-renewal]

Options:
  --force-renewal  Force certbot to request a fresh certificate even if one already exists.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --force-renewal)
            FORCE_RENEWAL=true
            shift
            ;;
        --help|-h)
            print_usage
            exit 0
            ;;
        *)
            echo "[ERROR] Unknown argument: $1" >&2
            print_usage
            exit 1
            ;;
    esac
done

ensure_infrastructure

CERTBOT_ARGS=(
    run --rm certbot
    certonly
    --webroot
    -w /var/www/certbot
    --email "$ACME_EMAIL"
    --agree-tos
    --no-eff-email
    --cert-name "$CERT_NAME"
    -d "$WEBSITE_DOMAIN"
    -d "www.$WEBSITE_DOMAIN"
    -d "$API_DOMAIN"
    -d "$STUDIO_DOMAIN"
)

if [[ "$FORCE_RENEWAL" == "true" ]]; then
    CERTBOT_ARGS+=(--force-renewal)
else
    CERTBOT_ARGS+=(--keep-until-expiring)
fi

echo "[INFO] Requesting certificate bundle '$CERT_NAME' for $WEBSITE_DOMAIN, www.$WEBSITE_DOMAIN, $API_DOMAIN, and $STUDIO_DOMAIN"
remove_bootstrap_placeholder_certificate

if ! docker compose -f "$COMPOSE_FILE" "${CERTBOT_ARGS[@]}"; then
    echo "[ERROR] Certbot issuance failed. Restoring the temporary bootstrap certificate files." >&2
    ensure_placeholder_certificate
    exit 1
fi

echo "[INFO] Reloading nginx to pick up the latest certificates"
nginx_reload

echo "[INFO] Certificate workflow completed"
