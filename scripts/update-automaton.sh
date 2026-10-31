#!/usr/bin/env bash
# Upgrade a standalone Automaton server from a checksum-verified release bundle.
set -euo pipefail
umask 077

install_dir=/opt/automaton-server
config_dir=/etc/automaton
backup_root=''
version=''
bundle_archive=''
bundle_checksum=''
website_image=''
website_compose=''
website_service=''
website_url=''
while (($#)); do
    case "$1" in
        --version) version="${2:?Missing release version}"; shift 2 ;;
        --bundle) bundle_archive="${2:?Missing release bundle}"; shift 2 ;;
        --checksum) bundle_checksum="${2:?Missing checksum}"; shift 2 ;;
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --config-dir) config_dir="${2:?Missing configuration directory}"; shift 2 ;;
        --backup-dir) backup_root="${2:?Missing backup directory}"; shift 2 ;;
        --website-image) website_image="${2:?Missing website image}"; shift 2 ;;
        --website-compose) website_compose="${2:?Missing website Compose file}"; shift 2 ;;
        --website-service) website_service="${2:?Missing website service}"; shift 2 ;;
        --website-url) website_url="${2:?Missing website URL}"; shift 2 ;;
        --help) echo 'Usage: sudo bash update-automaton.sh [--version server-vX.Y.Z] [--bundle automaton-server.tar.gz --checksum automaton-server.tar.gz.sha256] [--install-dir PATH] [--config-dir PATH] [--backup-dir PATH]'; echo 'Website: sudo bash update-automaton.sh --website-image IMAGE@sha256:DIGEST --website-compose FILE --website-service SERVICE --website-url HTTPS_URL'; echo 'Without --version, selects the latest stable published server distribution. Offline bundles require --version.'; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done
[[ $EUID == 0 ]] || { echo 'Run the updater with sudo.' >&2; exit 1; }
if [[ -n "$website_image$website_compose$website_service$website_url" ]]; then
    [[ -n "$website_image" && -n "$website_compose" && -n "$website_service" && -n "$website_url" && -z "$version$bundle_archive$bundle_checksum$backup_root" ]] || { echo 'Specify all website options without server release options.' >&2; exit 1; }
    [[ "$website_image" =~ ^[a-zA-Z0-9_./:-]+@sha256:[a-f0-9]{64}$ ]] || { echo 'Use an immutable website image digest.' >&2; exit 1; }
    [[ "$website_compose" =~ ^/[a-zA-Z0-9_./-]+$ && -f "$website_compose" && ! -L "$website_compose" ]] || { echo 'Use an absolute, regular website Compose file.' >&2; exit 1; }
    [[ "$website_service" =~ ^[a-zA-Z0-9_-]+$ && "$website_url" =~ ^https://[a-zA-Z0-9.-]+/?$ ]] || { echo 'Use a valid website service and HTTPS URL.' >&2; exit 1; }
    command -v docker >/dev/null && docker compose version >/dev/null && docker info >/dev/null
    command -v python3 >/dev/null && command -v curl >/dev/null
    exec 9>/run/lock/automaton-install.lock
    flock -n 9 || { echo 'Another installation or update is running.' >&2; exit 1; }
    compose_dir=$(dirname -- "$website_compose")
    compose=(docker compose --project-directory "$compose_dir" -f "$website_compose")
    "${compose[@]}" config --quiet
    current_container=$("${compose[@]}" ps -q "$website_service")
    [[ -n "$current_container" && "$(docker inspect -f '{{.State.Running}}' "$current_container")" == true ]] || { echo 'Website service is not running.' >&2; exit 1; }
    current_image=$("${compose[@]}" config --format json | python3 -c 'import json,sys; print(json.load(sys.stdin)["services"][sys.argv[1]]["image"])' "$website_service")
    [[ "$current_image" =~ ^[a-zA-Z0-9_./:-]+$ ]] || { echo 'Website service image must be a tag.' >&2; exit 1; }
    docker pull "$website_image"
    docker tag "$(docker inspect -f '{{.Image}}' "$current_container")" "$current_image-previous"
    docker tag "$website_image" "$current_image"
    "${compose[@]}" up -d --no-deps --no-build --wait --wait-timeout 120 "$website_service"
    curl --fail --silent --show-error --retry 12 --retry-delay 5 --retry-all-errors "$website_url" >/dev/null
    echo "Updated $website_service to $website_image and verified $website_url"
    exit 0
fi
[[ -n "$version" || -z "$bundle_archive" ]] || { echo 'Specify --version when using an offline bundle.' >&2; exit 1; }
[[ -z "$version" || "$version" =~ ^server-v[0-9]+\.[0-9]+\.[0-9]+([.-][a-zA-Z0-9.-]+)?$ ]] || { echo 'Specify --version server-vX.Y.Z.' >&2; exit 1; }
for dir in "$install_dir" "$config_dir"; do
    [[ "$dir" =~ ^/[a-zA-Z0-9_./-]+$ && "$dir" != / && ! -L "$dir" ]] || { echo 'Use absolute, non-symlink installation and configuration directories.' >&2; exit 1; }
done
backup_root=${backup_root:-$install_dir/backups}
[[ "$backup_root" =~ ^/[a-zA-Z0-9_./-]+$ && "$backup_root" != / && ! -L "$backup_root" ]] || { echo 'Use an absolute, non-symlink backup directory.' >&2; exit 1; }
[[ "$(realpath -m "$backup_root")" != "$(realpath -m "$config_dir")"* ]] || { echo 'Keep backups outside the live configuration directory.' >&2; exit 1; }
[[ -z "$bundle_archive" || -n "$bundle_checksum" ]] || { echo 'An offline bundle needs --checksum.' >&2; exit 1; }
[[ -n "$bundle_archive" || -z "$bundle_checksum" ]] || { echo '--checksum needs --bundle.' >&2; exit 1; }

exec 9>/run/lock/automaton-install.lock
flock -n 9 || { echo 'Another installation or update is running.' >&2; exit 1; }
for file in "$install_dir/release.env" "$install_dir/compose.yaml" "$install_dir/.env" "$config_dir/installation.json" "$config_dir/appsettings.Production.json"; do
    [[ -f "$file" && ! -L "$file" ]] || { echo "Existing installation is incomplete: $file. Restore it from backup." >&2; exit 1; }
done
grep -Fxq "CONFIG_DIR=$config_dir" "$install_dir/.env" || { echo 'The installation uses another configuration directory.' >&2; exit 1; }
command -v docker >/dev/null && docker compose version >/dev/null && docker info >/dev/null
command -v python3 >/dev/null && command -v curl >/dev/null && command -v sha256sum >/dev/null
if [[ -z "$version" ]]; then
    registry='rzvdaniel/automaton-server-distribution'
    token=$(curl --fail --silent --show-error --proto '=https' --tlsv1.2 "https://ghcr.io/token?scope=repository:$registry:pull" |
        python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')
    version=$(curl --fail --silent --show-error --proto '=https' --tlsv1.2 \
        --header "Authorization: Bearer $token" "https://ghcr.io/v2/$registry/tags/list?n=10000" |
        python3 -c 'import json,re,sys; print("\n".join(tag for tag in json.load(sys.stdin).get("tags", []) if re.fullmatch(r"server-v[0-9]+\.[0-9]+\.[0-9]+", tag)))' |
        sort -V | tail -n 1)
    [[ -n "$version" ]] || { echo 'No stable server release was found in the public distribution package.' >&2; exit 1; }
    printf 'Updating to latest stable Automaton server release: %s\n' "$version"
fi

working=$(mktemp -d)
trap 'rm -rf -- "$working"' EXIT
if [[ -z "$bundle_archive" ]]; then
    image="ghcr.io/rzvdaniel/automaton-server-distribution:$version"
    docker pull "$image"
    distribution=$(docker create "$image")
    trap 'docker rm "$distribution" >/dev/null; rm -rf -- "$working"' EXIT
    docker cp "$distribution:/automaton-server.tar.gz" "$working/automaton-server.tar.gz"
    docker cp "$distribution:/automaton-server.tar.gz.sha256" "$working/automaton-server.tar.gz.sha256"
    docker rm "$distribution" >/dev/null
    trap 'rm -rf -- "$working"' EXIT
else
    [[ -f "$bundle_archive" && -f "$bundle_checksum" ]] || { echo 'Bundle or checksum file is missing.' >&2; exit 1; }
    cp -- "$bundle_archive" "$working/automaton-server.tar.gz"
    cp -- "$bundle_checksum" "$working/automaton-server.tar.gz.sha256"
fi
python3 - "$working/automaton-server.tar.gz.sha256" <<'PY'
import re, sys
if not re.fullmatch(r'[a-fA-F0-9]{64}  automaton-server\.tar\.gz\n?', open(sys.argv[1]).read()):
    raise SystemExit('Invalid release checksum file.')
PY
(cd "$working" && sha256sum -c automaton-server.tar.gz.sha256)
python3 - "$working/automaton-server.tar.gz" "$working" <<'PY'
import pathlib, sys, tarfile
with tarfile.open(sys.argv[1], 'r:gz') as archive:
    for member in archive.getmembers():
        parts = pathlib.PurePosixPath(member.name).parts
        if not parts or parts[0] != 'automaton-server' or '..' in parts or not (member.isdir() or member.isfile()):
            raise SystemExit('Invalid release bundle entry.')
    archive.extractall(sys.argv[2], filter='data')
PY
bundle="$working/automaton-server"
[[ -f "$bundle/release.env" && -f "$bundle/compose.yaml" && -f "$bundle/version" && -f "$bundle/update.sh" && -f "$bundle/install.sh" ]] || { echo 'The release bundle is incomplete.' >&2; exit 1; }
[[ "$(cat "$bundle/version")" == "$version" ]] || { echo 'Release version does not match the bundle.' >&2; exit 1; }
if cmp -s "$bundle/release.env" "$install_dir/release.env"; then
    echo 'This release is already installed.'
    exit 0
fi
if [[ -f "$install_dir/version" ]]; then
    current_version=$(cat "$install_dir/version")
    [[ "$current_version" =~ ^server-v[0-9]+\.[0-9]+\.[0-9]+([.-][a-zA-Z0-9.-]+)?$ ]] || { echo 'Installed version marker is invalid.' >&2; exit 1; }
    dpkg --compare-versions "${version#server-v}" gt "${current_version#server-v}" || { echo 'Choose a newer server release.' >&2; exit 1; }
fi
cmp -s "$bundle/compose.yaml" "$install_dir/compose.yaml" || { echo 'This release changes Compose. A reviewed migration is required.' >&2; exit 1; }
python3 - "$install_dir/release.env" "$bundle/release.env" <<'PY'
import re, sys
def read(path):
    entries = dict(line.strip().split('=', 1) for line in open(path) if line.strip())
    required = {'API_IMAGE', 'WORKER_IMAGE', 'STUDIO_IMAGE', 'MYSQL_IMAGE', 'RABBITMQ_IMAGE', 'TRAEFIK_IMAGE', 'RUNNER_IMAGE'}
    if set(entries) != required or any(not re.fullmatch(r'[a-zA-Z0-9_./:-]+@sha256:[a-f0-9]{64}', value) for value in entries.values()):
        raise SystemExit('Invalid release image manifest.')
    return entries
old, new = read(sys.argv[1]), read(sys.argv[2])
for name in ('MYSQL_IMAGE', 'RABBITMQ_IMAGE', 'TRAEFIK_IMAGE', 'RUNNER_IMAGE'):
    if old[name] != new[name]:
        raise SystemExit(f'{name} changed. A reviewed infrastructure migration is required.')
PY

old_compose=(docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml")
new_compose=(docker compose --project-directory "$install_dir" --env-file "$bundle/release.env" --env-file "$install_dir/.env" -f "$bundle/compose.yaml")
override="$install_dir/compose.override.yaml"
if [[ -e "$override" || -L "$override" ]]; then
    [[ -f "$override" && ! -L "$override" ]] || { echo 'The installation Compose override must be a regular, non-symlink file.' >&2; exit 1; }
    old_compose+=(-f "$override")
    new_compose+=(-f "$override")
fi
"${old_compose[@]}" config --quiet
"${new_compose[@]}" config --quiet
for service in mysql rabbitmq api worker studio traefik; do
    container=$("${old_compose[@]}" ps -q "$service")
    [[ -n "$container" && "$(docker inspect -f '{{.State.Running}}' "$container")" == true ]] || { echo "$service is not running; repair the installation before updating." >&2; exit 1; }
done
disk_path=$backup_root
while [[ ! -d "$disk_path" ]]; do disk_path=$(dirname -- "$disk_path"); done
available_kb=$(df -Pk "$disk_path" | awk 'NR==2 {print $4}')
(( available_kb >= 10485760 )) || { echo 'At least 10 GiB free disk space is required before updating.' >&2; exit 1; }

domain=$(python3 - "$config_dir/installation.json" <<'PY'
import json, sys
print(json.load(open(sys.argv[1]))['domain'])
PY
)
curl --fail --silent --show-error "https://studio.$domain/api/install/status" | python3 -c 'import json,sys; assert json.load(sys.stdin)["installationComplete"]'

install -d -m 0700 "$backup_root"
backup=$(mktemp -d "$backup_root/$version-$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX")
echo "Creating protected recovery backup at $backup"
cp -p "$install_dir/release.env" "$install_dir/compose.yaml" "$install_dir/.env" "$backup/"
[[ ! -f "$override" ]] || cp -p "$override" "$backup/compose.override.yaml"
[[ ! -f "$install_dir/version" ]] || cp -p "$install_dir/version" "$backup/previous-version"
[[ ! -f "$install_dir/update.sh" ]] || cp -p "$install_dir/update.sh" "$backup/previous-update.sh"
[[ ! -f "$install_dir/install.sh" ]] || cp -p "$install_dir/install.sh" "$backup/previous-install.sh"
tar -C "$(dirname "$config_dir")" -czf "$backup/config.tar.gz" "$(basename "$config_dir")"
api_container=$("${old_compose[@]}" ps -q api)
worker_container=$("${old_compose[@]}" ps -q worker)
docker cp "$api_container:/app/data-protection-keys/." - | gzip > "$backup/api-keys.tar.gz"
docker cp "$worker_container:/app/data-protection-keys/." - | gzip > "$backup/worker-keys.tar.gz"
docker cp "$api_container:/app/AppData/." - | gzip > "$backup/api-data.tar.gz"
"${old_compose[@]}" exec -T mysql sh -ec 'MYSQL_PWD=$(cat /run/secrets/mysql_root_password); export MYSQL_PWD; exec mysqldump --single-transaction --quick --routines --triggers --events --no-tablespaces -u root automaton' > "$backup/database.sql"
[[ -s "$backup/database.sql" ]] && tail -n 1 "$backup/database.sql" | grep -q 'Dump completed' || { echo 'Database backup did not complete.' >&2; exit 1; }
for archive in config api-keys worker-keys api-data; do tar -tzf "$backup/$archive.tar.gz" >/dev/null; done
(cd "$backup" && sha256sum release.env compose.yaml .env config.tar.gz api-keys.tar.gz worker-keys.tar.gz api-data.tar.gz database.sql > SHA256SUMS && sha256sum -c SHA256SUMS >/dev/null)
if [[ -f "$override" ]]; then
    (cd "$backup" && sha256sum compose.override.yaml >> SHA256SUMS && sha256sum -c SHA256SUMS >/dev/null)
fi
echo 'Backup verified. Pulling release images.'
"${new_compose[@]}" pull api worker studio
echo 'Updating API, Worker and Studio.'
if ! "${new_compose[@]}" up -d --wait --wait-timeout 600 api worker studio; then
    echo "Update did not reach healthy state. Previous manifest and recovery backup remain at $backup. Do not retry until the database and running services are assessed." >&2
    exit 1
fi
if ! curl --fail --silent --show-error --retry 12 --retry-delay 5 --retry-all-errors "https://studio.$domain/api/install/status" | python3 -c 'import json,sys; assert json.load(sys.stdin)["installationComplete"]' \
    || ! curl --fail --silent --show-error "https://studio.$domain/" >/dev/null; then
    echo "Public route validation failed. Previous manifest and recovery backup remain at $backup. Assess the services before recovery." >&2
    exit 1
fi
install -m 0600 "$bundle/release.env" "$install_dir/release.env"
install -m 0644 "$bundle/compose.yaml" "$install_dir/compose.yaml"
install -m 0644 "$bundle/version" "$install_dir/version"
install -m 0700 "$bundle/update.sh" "$install_dir/update.sh"
install -m 0700 "$bundle/install.sh" "$install_dir/install.sh"
echo "Updated to $version. Recovery backup: $backup"
echo 'Copy the backup to encrypted off-host storage; keep the database and Data Protection keys together.'
