#!/bin/bash
# Renew existing production TLS certificates and reload nginx if successful.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "$SCRIPT_DIR/prod-blue-green-common.sh"

ensure_infrastructure

echo "[INFO] Running certbot renew"
docker compose -f "$COMPOSE_FILE" run --rm certbot renew --webroot -w /var/www/certbot

echo "[INFO] Reloading nginx after certbot renew"
nginx_reload

echo "[INFO] Certificate renewal workflow completed"
