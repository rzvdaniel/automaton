#!/usr/bin/env bash
# Configure the production Traefik edge and Cloudflare DNS-01 credentials.
# Run this on the production host from the Automaton repository root.

set -euo pipefail
umask 077

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"
API_SETTINGS_FILE="$REPO_ROOT/config/appsettings.Production.json"
RUNTIME_DIR="$REPO_ROOT/.prod-runtime/traefik"

if [[ ! -f "$REPO_ROOT/docker-compose.prod.yml" ]]; then
    echo "[ERROR] Run this script from an Automaton repository checkout." >&2
    exit 1
fi

prompt_value() {
    local key="$1"
    local description="$2"
    local default_value="${3:-}"
    local value

    echo
    echo "$key"
    echo "$description"
    if [[ -n "$default_value" ]]; then
        read -r -p "Value [$default_value]: " value
        value="${value:-$default_value}"
    else
        read -r -p "Value: " value
    fi

    if [[ -z "$value" ]]; then
        echo "[ERROR] $key is required." >&2
        exit 1
    fi

    printf '%s' "$value"
}

prompt_secret() {
    local key="$1"
    local description="$2"
    local default_value="${3:-}"
    local value

    echo
    echo "$key"
    echo "$description"
    if [[ -n "$default_value" ]]; then
        read -r -s -p "Value [press Enter to use generated value]: " value
        echo
        value="${value:-$default_value}"
    else
        read -r -s -p "Value: " value
        echo
    fi

    if [[ -z "$value" ]]; then
        echo "[ERROR] $key is required." >&2
        exit 1
    fi

    printf '%s' "$value"
}

upsert_env() {
    local key="$1"
    local value="$2"
    local temp_file
    temp_file="$(mktemp "${ENV_FILE}.tmp.XXXXXX")"

    if [[ -f "$ENV_FILE" ]]; then
        grep -v -E "^${key}=" "$ENV_FILE" > "$temp_file" || true
    fi
    printf '%s=%s\n' "$key" "$value" >> "$temp_file"
    mv "$temp_file" "$ENV_FILE"
}

if [[ ! -f "$ENV_FILE" ]]; then
    echo "[INFO] Creating $ENV_FILE from .env.prod.example"
    cp "$REPO_ROOT/.env.prod.example" "$ENV_FILE"
fi

if [[ ! -f "$API_SETTINGS_FILE" ]]; then
    echo "[ERROR] Missing API production settings file: $API_SETTINGS_FILE" >&2
    echo "Create it from the secured production configuration before continuing." >&2
    exit 1
fi

echo "Automaton production Traefik bootstrap"
echo "======================================="
echo "This script writes production domains and secrets to: $ENV_FILE"
echo "The API settings file is kept separately at: $API_SETTINGS_FILE"
echo "It does not deploy API, Studio, Website, or runners."
echo
echo "Cloudflare token instructions:"
echo "  1. Open Cloudflare Dashboard -> Profile -> API Tokens."
echo "  2. Create a custom token for the Automaton DNS zone."
echo "  3. Grant only Zone:Read and DNS:Edit for that zone."
echo "  4. Copy the token when Cloudflare displays it; it cannot be viewed later."
echo "  Documentation: https://developers.cloudflare.com/fundamentals/api/get-started/create-token/"

api_domain="$(prompt_value API_DOMAIN 'Public API hostname. This is normally the api subdomain of your production domain.' "${API_DOMAIN:-api.automaton.monster}")"
studio_domain="$(prompt_value STUDIO_DOMAIN 'Public Studio hostname. This is normally the studio subdomain.' "${STUDIO_DOMAIN:-studio.automaton.monster}")"
website_domain="$(prompt_value WEBSITE_DOMAIN 'Apex website hostname, without https://.' "${WEBSITE_DOMAIN:-automaton.monster}")"
apps_domain="$(prompt_value APPS_DOMAIN 'Base app hostname, without https://. Published apps use slug.apps_domain.' "${APPS_DOMAIN:-apps.automaton.monster}")"
acme_email="$(prompt_value ACME_EMAIL 'Email used by Let’s Encrypt for expiry and ACME notices.' "${ACME_EMAIL:-}")"
cloudflare_token="$(prompt_secret CF_DNS_API_TOKEN 'Cloudflare API token created with only Zone:Read and DNS:Edit for the Automaton zone.' "${CF_DNS_API_TOKEN:-}")"
runtime_key="$(prompt_secret RUNTIME_PROXY_AUTH_KEY 'Shared secret used by the API when authenticating runner proxy traffic. Generate a new random value unless preserving an existing production key.' "${RUNTIME_PROXY_AUTH_KEY:-$(openssl rand -hex 32)}")"

upsert_env API_DOMAIN "$api_domain"
upsert_env STUDIO_DOMAIN "$studio_domain"
upsert_env WEBSITE_DOMAIN "$website_domain"
upsert_env APPS_DOMAIN "$apps_domain"
upsert_env ACME_EMAIL "$acme_email"
upsert_env CF_DNS_API_TOKEN "$cloudflare_token"
upsert_env RUNTIME_PROXY_AUTH_KEY "$runtime_key"

mkdir -p "$RUNTIME_DIR/dynamic"
chmod 700 "$RUNTIME_DIR" "$RUNTIME_DIR/dynamic"

echo
echo "[INFO] Validating Docker Compose configuration"
docker compose --env-file "$ENV_FILE" -f "$REPO_ROOT/docker-compose.prod.yml" config >/dev/null

echo
read -r -p "Start or recreate Traefik now? [y/N] " start_traefik
if [[ "$start_traefik" =~ ^[Yy]$ ]]; then
    docker compose --env-file "$ENV_FILE" -f "$REPO_ROOT/docker-compose.prod.yml" up -d traefik
    docker compose --env-file "$ENV_FILE" -f "$REPO_ROOT/docker-compose.prod.yml" exec -T traefik \
        wget -q -O- http://127.0.0.1:8082/ping >/dev/null
    echo "[INFO] Traefik is running and healthy."
fi

echo
echo "[OK] Traefik bootstrap configuration completed."
echo "Next steps:"
echo "  1. Verify Cloudflare DNS records and SSL mode Full (strict)."
echo "  2. Run ./scripts/prod-redeploy-all.sh --keep-previous"
echo "  3. Run ./scripts/prod-verify.sh"
