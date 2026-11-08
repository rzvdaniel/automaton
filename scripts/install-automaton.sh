#!/usr/bin/env bash
# Install a versioned Automaton server release. Re-running preserves configuration/data.
set -euo pipefail
umask 077
original_args=("$@")
install_dir=/opt/automaton-server
config_dir=/etc/automaton
domain=''
email=''
version=''
tls_mode=http
configure_only=false
quiet=false
status_only=false
switch_tls=false
tls_mode_given=false
token_file=''
public_ips=()
bundle_dir="${AUTOMATON_RELEASE_DIR:-$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)}"
[[ -f "$bundle_dir/configure.py" ]] || bundle_dir="$bundle_dir/../docker/install"
while (($#)); do
    case "$1" in
        --domain) domain="${2:?Missing domain}"; shift 2 ;;
        --public-ip) public_ips+=("${2:?Missing public IP}"); shift 2 ;;
        --email) email="${2:?Missing email}"; shift 2 ;;
        --tls-mode) tls_mode="${2:?Missing TLS mode}"; tls_mode_given=true; shift 2 ;;
        --switch-tls) switch_tls=true; shift ;;
        --cloudflare-token-file) token_file="${2:?Missing token file}"; shift 2 ;;
        --version) version="${2:?Missing release tag}"; shift 2 ;;
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --config-dir) config_dir="${2:?Missing configuration directory}"; shift 2 ;;
        --install-docker) shift ;; # Accepted for commands written before Docker installation became automatic.
        --configure-only) configure_only=true; shift ;;
        --quiet) quiet=true; shift ;;
        --status) status_only=true; shift ;;
        --help) printf '%s\n' 'Usage: sudo bash install-automaton.sh [--version server-vVERSION] [--domain example.com] [--email admin@example.com]' 'Installs Docker Engine and Compose automatically on Ubuntu and Debian if missing (elsewhere, install Docker first). Existing Docker installations are preserved.' 'Without --version, a standalone installer selects the latest stable public server release; an extracted bundle installs its own version.' 'Optional: --install-dir PATH --config-dir PATH --public-ip ADDRESS --configure-only --status --quiet (do not print the setup link; read it from the secrets/setup-token file)' 'Requires x86_64 Linux with systemd, public DNS and ports 80/443/5671. Optional --tls-mode cloudflare (Cloudflare DNS token) or delegated (one NS record, port 53; no token) enables wildcard certificates.' 'Switch an installed server: sudo bash /opt/automaton-server/install.sh --switch-tls --tls-mode cloudflare|delegated|http [--cloudflare-token-file PATH] (token via hidden prompt or AUTOMATON_CLOUDFLARE_TOKEN otherwise)'; exit 0 ;;
        *) printf 'Unknown option: %s\n' "$1" >&2; exit 1 ;;
    esac
done
[[ $EUID == 0 ]] || { echo 'Run the installer with sudo.' >&2; exit 1; }
for dir in "$install_dir" "$config_dir"; do
    [[ "$dir" =~ ^/[a-zA-Z0-9_./-]+$ && "$dir" != / ]] || { echo 'Use an absolute directory path without shell metacharacters.' >&2; exit 1; }
    [[ ! -L "$dir" ]] || { echo 'Installation/configuration directories must not be symlinks.' >&2; exit 1; }
done
exec 9>/run/lock/automaton-install.lock
flock -n 9 || { echo 'Another installer is running.' >&2; exit 1; }
unset API_IMAGE WORKER_IMAGE STUDIO_IMAGE STORE_IMAGE POSTGRES_IMAGE RABBITMQ_IMAGE TRAEFIK_IMAGE RUNNER_IMAGE
compose=(docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml")
if [[ -e "$install_dir/compose.override.yaml" || -L "$install_dir/compose.override.yaml" ]]; then
    [[ -f "$install_dir/compose.override.yaml" && ! -L "$install_dir/compose.override.yaml" ]] || { echo 'The installation Compose override must be a regular, non-symlink file.' >&2; exit 1; }
    compose+=(-f "$install_dir/compose.override.yaml")
fi
if $status_only; then
    "${compose[@]}" ps
    exit
fi
verify_https() {
echo 'Waiting for HTTPS certificates…'
explain_https_failure() {
    printf 'The HTTPS check failed at %s. The services may be healthy, but HTTPS is not ready.\n' "$1" >&2
    "${compose[@]}" logs --no-color --since "$certificate_attempt_started" --tail 300 traefik 2>/dev/null \
        | python3 "$bundle_dir/explain_certificate_failure.py" "$domain" || true
    exit 1
}
# Older bundles use /healthz and predate the public /health check.
if grep -Fq 'http://127.0.0.1:8080/health]' "$install_dir/compose.yaml"; then
    api_health=$(curl --fail --silent --retry 12 --retry-delay 10 --retry-all-errors "https://api.$domain/health" 2>/dev/null) \
        || explain_https_failure "https://api.$domain/health"
    printf '%s' "$api_health" | python3 -c 'import json,sys; t=sys.stdin.read().strip(); assert t == "ok" or json.loads(t)["status"] == "ok"' 2>/dev/null || { echo "Unexpected response from https://api.$domain/health." >&2; exit 1; }
fi
studio_status=$(curl --fail --silent --retry 12 --retry-delay 10 --retry-all-errors "https://studio.$domain/api/install/status" 2>/dev/null) \
    || explain_https_failure "https://studio.$domain/api/install/status"
python3 -c 'import json,sys; response=json.load(sys.stdin); assert "installationComplete" in response' <<< "$studio_status" 2>/dev/null \
    || { echo 'The Studio installation status response was invalid.' >&2; exit 1; }
curl --fail --silent --retry 6 --retry-delay 10 --retry-all-errors "https://studio.$domain/" >/dev/null \
    || explain_https_failure "https://studio.$domain/"
curl --fail --silent --retry 6 --retry-delay 10 --retry-all-errors "https://store.$domain/" >/dev/null \
    || printf 'Warning: the app store did not answer at https://store.%s/. Check its DNS record; the rest of the installation is complete.\n' "$domain" >&2
python3 - "$domain" <<'PYBROKER'
import socket, ssl, sys
with socket.create_connection(('127.0.0.1', 5671), timeout=10) as raw:
    with ssl.create_default_context().wrap_socket(raw, server_hostname='api.' + sys.argv[1]) as broker:
        broker.sendall(b'AMQP\x00\x00\x09\x01')
        if not broker.recv(8): raise SystemExit('The Runner broker TLS route did not respond.')
PYBROKER
}
# Delegated mode (#891): the DNS responder runs in a Compose profile; DOMAIN, the profile and the port 53 address live in .env.
set_delegated_env() {
    local env_file="$install_dir/.env" bind=''
    local lines; lines=$(grep -Ev '^(DOMAIN|COMPOSE_PROFILES|DNS_BIND)=' "$env_file" || true)
    { [[ -z "$lines" ]] || printf '%s\n' "$lines"
      if [[ "$tls_mode" == delegated ]]; then
          bind=$(python3 "$bundle_dir/check_dns.py" --bind-address ${public_ips[@]+"${public_ips[@]}"}) || bind=''
          printf 'DOMAIN=%s\nCOMPOSE_PROFILES=delegated\n' "$domain"
          [[ -z "$bind" ]] || printf 'DNS_BIND=%s\n' "$bind"
      fi; } > "$env_file.new"
    chmod 0600 "$env_file.new"; mv "$env_file.new" "$env_file"
}
check_delegated_ready() {
    python3 "$bundle_dir/check_dns.py" --delegation "$domain" || exit 1
    if ss -lunH | awk '{print $4}' | grep -Ev '^(127\.|\[::1\])' | grep -Eq ':53$' && ! "${compose[@]}" ps --services --status running 2>/dev/null | grep -qx acme-responder; then
        echo 'Port 53 is already used on this server (often systemd-resolved listening on all addresses). Free it or pass --public-ip ADDRESS so the responder binds only that address; nothing was changed.' >&2
        exit 1
    fi
}
if $switch_tls; then
    [[ -f "$install_dir/release.env" && -f "$install_dir/.env" ]] || { echo 'No installation was found to switch. Run the installer without --switch-tls first.' >&2; exit 1; }
    [[ -f "$install_dir/configure.py" && -f "$install_dir/explain_certificate_failure.py" ]] || { echo "This server's installed tools predate the TLS switch. Update to a release that includes it first: sudo bash $install_dir/update.sh" >&2; exit 1; }
    bundle_dir="$install_dir"
    $tls_mode_given || { echo 'Say which mode to switch to: --switch-tls --tls-mode cloudflare (or http).' >&2; exit 1; }
    [[ -z "$domain$email" ]] || { echo '--switch-tls changes only the TLS mode; the domain and email cannot be changed.' >&2; exit 1; }
    command -v python3 >/dev/null && docker info >/dev/null || { echo 'Docker and python3 are required.' >&2; exit 1; }
    token_args=()
    [[ -z "$token_file" ]] || token_args=(--cloudflare-token-file "$token_file")
    domain=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["domain"])' "$config_dir/installation.json")
    export CONFIG_DIR="$config_dir"
    [[ "$tls_mode" != delegated ]] || check_delegated_ready
    python3 "$install_dir/configure.py" --config-dir "$config_dir" --switch-tls --tls-mode "$tls_mode" ${token_args[@]+"${token_args[@]}"} || exit 1
    "${compose[@]}" --profile delegated stop acme-responder >/dev/null 2>&1 || true
    set_delegated_env
    "${compose[@]}" config --quiet
    if [[ "$tls_mode" == delegated ]]; then "${compose[@]}" up -d acme-responder || exit 1; else "${compose[@]}" --profile delegated rm -f acme-responder >/dev/null 2>&1 || true; fi
    certificate_attempt_started=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    # Always restart and check, so a switch that failed its HTTPS check can be retried with the same command.
    # Traefik reads its certificate resolver at start; the API rewrites published app routes with the new resolver at start.
    "${compose[@]}" restart traefik api
    verify_https
    if [[ "$tls_mode" == delegated ]]; then
        echo "TLS mode is delegated: published apps use *.apps.$domain. Let's Encrypt asks this server's DNS responder (port 53) for the challenge; the one record is: $(python3 -c 'import sys; sys.path.insert(0, sys.argv[1]); import check_dns; print(check_dns.delegation_record(sys.argv[2]))' "$bundle_dir" "$domain")"
    elif [[ "$tls_mode" == cloudflare ]]; then
        echo "TLS mode is cloudflare: published apps use *.apps.$domain. Traefik requests the wildcard certificate on first use through the Cloudflare DNS challenge."
    else
        echo 'TLS mode is http (HTTP-validated certificates). The Cloudflare token file is empty; port 80 must stay reachable.'
    fi
    exit 0
fi
if [[ -e "$install_dir/compose.override.yaml" || -L "$install_dir/compose.override.yaml" ]]; then
    echo 'This installation has reviewed Compose overrides. Use update.sh for releases; the fresh installer cannot reconfigure it.' >&2
    exit 1
fi
# Check the host before installing bootstrap dependencies or downloading a release.
[[ "$(uname -s)" == Linux && "$(uname -m)" == x86_64 ]] || { echo "Automaton needs an x86_64 Linux server (found $(uname -s) $(uname -m))." >&2; exit 1; }
[[ -d /run/systemd/system ]] || { echo 'Automaton needs a Linux server that runs systemd.' >&2; exit 1; }
ID=''; ID_LIKE=''; VERSION_CODENAME=''; PRETTY_NAME='this Linux distribution'
[[ -r /etc/os-release ]] && source /etc/os-release
# Packages are installed automatically only where apt is available; elsewhere the person installs them first.
install_packages() {
    if command -v apt-get >/dev/null; then
        apt-get update
        DEBIAN_FRONTEND=noninteractive apt-get install -y "$@"
    else
        echo "Install these packages with your package manager, then run the installer again: $*" >&2
        exit 1
    fi
}
# Docker publishes apt repositories for Ubuntu and Debian themselves (not for derivatives with their own codenames).
docker_apt_distribution=''
[[ "$ID" == ubuntu || "$ID" == debian ]] && docker_apt_distribution=$ID
# Download the complete release when this script was downloaded by itself.
if [[ ! -f "$bundle_dir/release.env" ]]; then
    if ! command -v python3 >/dev/null || ! command -v curl >/dev/null; then
        install_packages ca-certificates curl python3
    fi
    if [[ -z "$version" ]]; then
        version=$(python3 - <<'PY'
import json, re, urllib.request
versions = []
for page in range(1, 100):
    url = f'https://api.github.com/repos/rzvdaniel/automaton/releases?per_page=100&page={page}'
    request = urllib.request.Request(url, headers={'User-Agent': 'automaton-server-installer'})
    with urllib.request.urlopen(request, timeout=20) as response:
        releases = json.load(response)
    if not releases:
        break
    versions.extend(release['tag_name'] for release in releases
                    if not release['draft'] and not release['prerelease']
                    and re.fullmatch(r'server-v[0-9]+\.[0-9]+\.[0-9]+', release['tag_name']))
else:
    raise SystemExit('Too many releases to select the latest server version.')
if versions:
    print(max(versions, key=lambda version: tuple(map(int, version.removeprefix('server-v').split('.')))))
PY
)
        [[ -n "$version" ]] || { echo 'No published stable Automaton server release was found.' >&2; exit 1; }
        printf 'Installing latest stable Automaton server release: %s\n' "$version"
    fi
    [[ "$version" =~ ^server-v[0-9]+\.[0-9]+\.[0-9]+([.-][a-zA-Z0-9.-]+)?$ ]] || { echo 'Specify a published --version server-vX.Y.Z, or use a complete release bundle.' >&2; exit 1; }
    download_dir=$(mktemp -d)
    trap 'rm -rf -- "$download_dir"' EXIT
    release_url="https://github.com/rzvdaniel/automaton/releases/download/$version"
    curl --fail --location --proto '=https' --tlsv1.2 "$release_url/automaton-server.tar.gz" -o "$download_dir/automaton-server.tar.gz"
    curl --fail --location --proto '=https' --tlsv1.2 "$release_url/automaton-server.tar.gz.sha256" -o "$download_dir/automaton-server.tar.gz.sha256"
    (cd "$download_dir" && sha256sum -c automaton-server.tar.gz.sha256)
    tar -xzf "$download_dir/automaton-server.tar.gz" -C "$download_dir"
    bundle_dir="$download_dir/automaton-server"
fi
# Do not replace an existing Docker engine.
ensure_docker_repository() {
    if [[ -z "$docker_apt_distribution" || -z "$VERSION_CODENAME" ]] || ! command -v apt-get >/dev/null; then
        echo "Install Docker Engine and the Docker Compose plugin on $PRETTY_NAME (https://docs.docker.com/engine/install/), then run the installer again." >&2
        exit 1
    fi
    install_packages ca-certificates curl
    install -m 0755 -d /etc/apt/keyrings
    curl --fail --proto '=https' "https://download.docker.com/linux/$docker_apt_distribution/gpg" -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    printf 'deb [arch=%s signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/%s %s stable\n' "$(dpkg --print-architecture)" "$docker_apt_distribution" "$VERSION_CODENAME" > /etc/apt/sources.list.d/automaton-docker.list
    apt-get update
}
if ! command -v docker >/dev/null; then
    ensure_docker_repository
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
    systemctl enable --now docker
fi
docker info >/dev/null
if ! docker compose version >/dev/null; then
    command -v apt-get >/dev/null || { echo 'Install the Docker Compose plugin (https://docs.docker.com/compose/install/linux/), then run the installer again.' >&2; exit 1; }
    if apt-cache show docker-compose-v2 >/dev/null 2>&1; then
        apt-get install -y docker-compose-v2
    else
        if ! apt-cache show docker-compose-plugin >/dev/null 2>&1; then
            ensure_docker_repository
        fi
        apt-get install -y docker-compose-plugin
    fi
    docker compose version >/dev/null
fi
command -v python3 >/dev/null && python3 -c 'import cryptography' 2>/dev/null || install_packages python3 python3-cryptography
if [[ -f "$install_dir/release.env" ]] && ! cmp -s "$bundle_dir/release.env" "$install_dir/release.env"; then
    echo 'A different release is already installed. This command resumes installation; an upgrade requires a separate planned migration.' >&2
    exit 1
fi
if [[ -f "$install_dir/compose.yaml" ]] && ! cmp -s "$bundle_dir/compose.yaml" "$install_dir/compose.yaml"; then
    echo 'The installed Compose file has been modified. Review it before resuming; it was preserved.' >&2
    exit 1
fi
if [[ -f "$install_dir/.env" ]] && ! grep -Fxq "CONFIG_DIR=$config_dir" "$install_dir/.env"; then
    echo 'This installation uses another configuration directory. Existing settings were preserved.' >&2
    exit 1
fi
if [[ -f "$install_dir/release.env" ]] && [[ ! -f "$config_dir/installation.json" || ! -f "$config_dir/appsettings.Production.json" ]]; then
    echo 'Existing installation configuration is missing. Restore the configuration backup; credentials will not be regenerated.' >&2
    exit 1
fi
# A missing release/configuration directory must not cause new credentials to be
# generated against surviving database/key volumes from an older installation.
if [[ ! -f "$install_dir/release.env" ]]; then
    if [[ -n "$(docker ps -aq --filter label=com.docker.compose.project=automaton-server)" || -n "$(docker volume ls -q --filter label=com.docker.compose.project=automaton-server)" ]]; then
        echo 'Existing Automaton containers/volumes were found without this installation manifest. Restore the manifest and credentials from backup.' >&2
        exit 1
    fi
fi
# Do not collide with an unrelated Compose installation or a server already on these ports.
if ! $configure_only && [[ ! -f "$install_dir/release.env" ]]; then
    disk_path="$install_dir"
    while [[ ! -d "$disk_path" ]]; do disk_path=$(dirname -- "$disk_path"); done
    available_kb=$(df -Pk "$disk_path" | awk 'NR==2 {print $4}')
    (( available_kb >= 10485760 )) || { echo 'At least 10 GiB free disk space is required.' >&2; exit 1; }
    if ss -ltnH | awk '{print $4}' | grep -Eq ':(80|443|5671)$'; then
        echo 'Ports 80, 443 and 5671 must be available. Existing services were left running.' >&2
        exit 1
    fi
fi
[[ -n "$domain" ]] || read -r -p 'Base domain (example.com): ' domain
[[ -n "$email" ]] || read -r -p 'Certificate notification email: ' email
if ! $configure_only && [[ -f "$bundle_dir/check_dns.py" ]]; then
    python3 "$bundle_dir/check_dns.py" "$domain" "${public_ips[@]}"
    if [[ "$tls_mode" == delegated ]]; then python3 "$bundle_dir/check_dns.py" --delegation "$domain" || exit 1; fi
fi
# The release contains immutable references for application and infrastructure images.
runner_image=$(python3 - "$bundle_dir/release.env" <<'PY'
import re, sys
required={'API_IMAGE','STUDIO_IMAGE','WORKER_IMAGE','POSTGRES_IMAGE','RABBITMQ_IMAGE','TRAEFIK_IMAGE','RUNNER_IMAGE'}
optional={'STORE_IMAGE','AGE_IMAGE'}
entries=dict(line.strip().split('=',1) for line in open(sys.argv[1]) if line.strip() and not line.startswith('#'))
if not required <= set(entries) or set(entries) - required - optional or any(not re.fullmatch(r'[a-zA-Z0-9_./:@-]+',v) for v in entries.values()):
    raise SystemExit('Invalid release image manifest.')
if any(not re.fullmatch(r'[a-zA-Z0-9_./:-]+@sha256:[a-f0-9]{64}', value) for value in entries.values()):
    raise SystemExit('Every release image must be pinned by a SHA-256 digest.')
print(entries['RUNNER_IMAGE'])
PY
)
python3 "$bundle_dir/configure.py" --config-dir "$config_dir" --domain "$domain" --email "$email" --tls-mode "$tls_mode" --runner-image "$runner_image"
install -d -m 0755 "$install_dir"
install -m 0600 "$bundle_dir/release.env" "$install_dir/release.env"
install -m 0644 "$bundle_dir/compose.yaml" "$install_dir/compose.yaml"
if [[ -f "$bundle_dir/version" ]]; then install -m 0644 "$bundle_dir/version" "$install_dir/version"; fi
if [[ -f "$bundle_dir/update.sh" ]]; then install -m 0700 "$bundle_dir/update.sh" "$install_dir/update.sh"; fi
if [[ -f "$bundle_dir/install.sh" ]]; then install -m 0700 "$bundle_dir/install.sh" "$install_dir/install.sh"; fi
for script in backup.sh restore.sh update-from-request.sh configure.py explain_certificate_failure.py acme_responder.py; do
    if [[ -f "$bundle_dir/$script" ]]; then install -m 0700 "$bundle_dir/$script" "$install_dir/$script"; fi
done
# Updates requested from Studio and the CLI: root-only directories and a systemd path unit. The API container mounts them.
if [[ -f "$install_dir/update-from-request.sh" ]] && ! $configure_only; then
    bash "$install_dir/update-from-request.sh" --install-units --install-dir "$install_dir" \
        || { echo "Updates from Studio could not be enabled; nothing was changed. Fix it, then rerun: sudo bash $install_dir/update-from-request.sh --install-units" >&2; exit 1; }
fi
export CONFIG_DIR="$config_dir"
# Persist only the configuration path alongside the release manifest, for future Compose commands.
printf 'CONFIG_DIR=%s\nBACKUP_DIR=%s/backups\n' "$config_dir" "$install_dir" > "$install_dir/.env"
set_delegated_env
compose=(docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml")
"${compose[@]}" config --quiet
printf 'DNS must resolve api.%s, studio.%s, store.%s, apps.%s and *.apps.%s to this server. Allow inbound TCP 80, 443 and 5671.\n' "$domain" "$domain" "$domain" "$domain" "$domain"
if $configure_only; then
    echo 'Configuration prepared. No services were started.'
    exit 0
fi
"${compose[@]}" pull
certificate_attempt_started=$(date -u +%Y-%m-%dT%H:%M:%SZ)
"${compose[@]}" up -d --wait --wait-timeout 600
# Validate actual public HTTPS/API routing as well as container health.
verify_https
if $quiet; then
    printf '\nHTTPS certificates are ready. The setup link is not printed (--quiet). Open https://studio.%s/install and read the token with sudo cat %s/secrets/setup-token.\n' "$domain" "$config_dir"
else
    setup_token=$(tr -d '\r\n' < "$config_dir/secrets/setup-token")
    printf '\nHTTPS certificates are ready. Open the first-administrator setup link:\nhttps://studio.%s/install#token=%s\n' "$domain" "$setup_token"
    printf 'For an older release that cannot open the link, visit https://studio.%s/install and read the token with sudo cat %s/secrets/setup-token.\n' "$domain" "$config_dir"
fi
printf 'After the first administrator and Runner connect, Server Console, Website Monitor and Heartbeat will appear in Apps from the official catalog. They remain unpublished and need separate permission approval. To opt out before connecting a Runner, set Catalog.InstallEssentialsOnFirstRunner to false in %s/appsettings.Production.json and restart the API. Remove an unwanted app from its Settings > Danger Zone.\n' "$config_dir"
# Nightly database and key backups are on by default; take the first one now.
if [[ -f "$install_dir/backup.sh" ]]; then
    bash "$install_dir/backup.sh" --install-dir "$install_dir" --install-timer && bash "$install_dir/backup.sh" --install-dir "$install_dir" \
        || echo "Warning: nightly backups could not be enabled. Run: sudo bash $install_dir/backup.sh --install-timer" >&2
fi
printf 'Backups: nightly database and key backups are kept in %s/backups/nightly. Also enable your provider'"'"'s server backups (snapshots) and copy %s off-host.\n' "$install_dir" "$config_dir"
