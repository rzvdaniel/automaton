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
install_docker=false
configure_only=false
status_only=false
bundle_dir="${AUTOMATON_RELEASE_DIR:-$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)}"
[[ -f "$bundle_dir/configure.py" ]] || bundle_dir="$bundle_dir/../docker/install"
while (($#)); do
    case "$1" in
        --domain) domain="${2:?Missing domain}"; shift 2 ;;
        --email) email="${2:?Missing email}"; shift 2 ;;
        --tls-mode) tls_mode="${2:?Missing TLS mode}"; shift 2 ;;
        --version) version="${2:?Missing release tag}"; shift 2 ;;
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --config-dir) config_dir="${2:?Missing configuration directory}"; shift 2 ;;
        --install-docker) install_docker=true; shift ;;
        --configure-only) configure_only=true; shift ;;
        --status) status_only=true; shift ;;
        --help) printf '%s\n' 'Usage: sudo bash install-automaton.sh --version server-vVERSION [--domain example.com] [--email admin@example.com] [--install-docker]' 'Optional: --install-dir PATH --config-dir PATH --configure-only --status' 'Requires Ubuntu 24.04 or newer, public DNS and ports 80/443/5671. Optional --tls-mode cloudflare enables wildcard certificates.'; exit 0 ;;
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
unset API_IMAGE WORKER_IMAGE STUDIO_IMAGE MYSQL_IMAGE RABBITMQ_IMAGE TRAEFIK_IMAGE RUNNER_IMAGE
if $status_only; then
    docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml" ps
    exit
fi
# Download the complete release when this script was downloaded by itself.
if [[ ! -f "$bundle_dir/release.env" ]]; then
    [[ "$version" =~ ^server-v[0-9]+\.[0-9]+\.[0-9]+([.-][a-zA-Z0-9.-]+)?$ ]] || { echo 'Specify a published --version server-vX.Y.Z, or use a complete release bundle.' >&2; exit 1; }
    command -v curl >/dev/null || { echo 'Install curl first (sudo apt-get install curl).' >&2; exit 1; }
    download_dir=$(mktemp -d)
    trap 'rm -rf -- "$download_dir"' EXIT
    release_url="https://github.com/rzvdaniel/automaton/releases/download/$version"
    curl --fail --location --proto '=https' --tlsv1.2 "$release_url/automaton-server.tar.gz" -o "$download_dir/automaton-server.tar.gz"
    curl --fail --location --proto '=https' --tlsv1.2 "$release_url/automaton-server.tar.gz.sha256" -o "$download_dir/automaton-server.tar.gz.sha256"
    (cd "$download_dir" && sha256sum -c automaton-server.tar.gz.sha256)
    tar -xzf "$download_dir/automaton-server.tar.gz" -C "$download_dir"
    bundle_dir="$download_dir/automaton-server"
fi
# Require Ubuntu 24.04 or newer; do not replace an existing Docker engine.
source /etc/os-release
if [[ "$ID" != ubuntu ]] || ! dpkg --compare-versions "$VERSION_ID" ge 24.04; then
    echo 'This installer supports Ubuntu 24.04 or newer.' >&2
    exit 1
fi
[[ "$(uname -m)" == x86_64 ]] || { echo 'The initial server release supports x86_64 servers.' >&2; exit 1; }
ensure_docker_repository() {
    apt-get update
    apt-get install -y ca-certificates curl
    install -m 0755 -d /etc/apt/keyrings
    curl --fail --proto '=https' https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    printf 'deb [arch=%s signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu %s stable\n' "$(dpkg --print-architecture)" "$VERSION_CODENAME" > /etc/apt/sources.list.d/automaton-docker.list
    apt-get update
}
if ! command -v docker >/dev/null; then
    $install_docker || { echo 'Docker is missing. Re-run with --install-docker to install it from the official repository.' >&2; exit 1; }
    ensure_docker_repository
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
    systemctl enable --now docker
fi
docker info >/dev/null
if ! docker compose version >/dev/null; then
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
command -v python3 >/dev/null && python3 -c 'import cryptography' 2>/dev/null || { apt-get update; apt-get install -y python3 python3-cryptography; }
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
# The release contains immutable references for application and infrastructure images.
runner_image=$(python3 - "$bundle_dir/release.env" <<'PY'
import re, sys
required={'API_IMAGE','STUDIO_IMAGE','WORKER_IMAGE','MYSQL_IMAGE','RABBITMQ_IMAGE','TRAEFIK_IMAGE','RUNNER_IMAGE'}
entries=dict(line.strip().split('=',1) for line in open(sys.argv[1]) if line.strip() and not line.startswith('#'))
if set(entries) != required or any(not re.fullmatch(r'[a-zA-Z0-9_./:@-]+',v) for v in entries.values()):
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
export CONFIG_DIR="$config_dir"
# Persist only the configuration path alongside the release manifest, for future Compose commands.
printf 'CONFIG_DIR=%s\n' "$config_dir" > "$install_dir/.env"
compose=(docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml")
"${compose[@]}" config --quiet
printf 'DNS must resolve api.%s, studio.%s, apps.%s and *.apps.%s to this server. Allow inbound TCP 80, 443 and 5671.\n' "$domain" "$domain" "$domain" "$domain"
if $configure_only; then
    echo 'Configuration prepared. No services were started.'
    exit 0
fi
"${compose[@]}" pull
"${compose[@]}" up -d --wait --wait-timeout 600
# Validate actual public HTTPS/API routing as well as container health.
curl --fail --silent --show-error --retry 12 --retry-delay 10 --retry-all-errors "https://studio.$domain/api/install/status" | python3 -c 'import json,sys; response=json.load(sys.stdin); assert "installationComplete" in response'
curl --fail --silent --show-error "https://studio.$domain/" >/dev/null
python3 - "$domain" <<'PYBROKER'
import socket, ssl, sys
with socket.create_connection(('127.0.0.1', 5671), timeout=10) as raw:
    with ssl.create_default_context().wrap_socket(raw, server_hostname='api.' + sys.argv[1]) as broker:
        broker.sendall(b'AMQP\x00\x00\x09\x01')
        if not broker.recv(8): raise SystemExit('The Runner broker TLS route did not respond.')
PYBROKER
printf '\nOpen https://studio.%s/install and enter the setup token from:\n  sudo cat %s/secrets/setup-token\n' "$domain" "$config_dir"
printf 'Protect %s and the Compose volumes with encrypted off-host backups before relying on this installation.\n' "$config_dir"
