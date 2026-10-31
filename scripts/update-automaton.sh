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
keep_backups=3
while (($#)); do
    case "$1" in
        --version) version="${2:?Missing release version}"; shift 2 ;;
        --bundle) bundle_archive="${2:?Missing release bundle}"; shift 2 ;;
        --checksum) bundle_checksum="${2:?Missing checksum}"; shift 2 ;;
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --config-dir) config_dir="${2:?Missing configuration directory}"; shift 2 ;;
        --backup-dir) backup_root="${2:?Missing backup directory}"; shift 2 ;;
        --keep-backups) keep_backups="${2:?Missing number of backups to keep}"; shift 2 ;;
        --help) echo 'Usage: sudo bash update-automaton.sh [--version server-vX.Y.Z] [--bundle automaton-server.tar.gz --checksum automaton-server.tar.gz.sha256] [--install-dir PATH] [--config-dir PATH] [--backup-dir PATH] [--keep-backups N]'; echo 'Without --version, selects the latest stable published server distribution. Offline bundles require --version.'; echo 'After a successful update, keeps the newest N update backups (default 3) and removes the previous release application images.'; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done
[[ $EUID == 0 ]] || { echo 'Run the updater with sudo.' >&2; exit 1; }
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
[[ "$keep_backups" =~ ^[1-9][0-9]*$ ]] || { echo '--keep-backups must be 1 or more.' >&2; exit 1; }

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
# Continue with the verified release's own updater, so changes to the update procedure itself
# apply in this update instead of the next one. It reuses this verified bundle offline.
if [[ -z "${AUTOMATON_UPDATER_FROM_BUNDLE:-}" ]] && ! cmp -s "$bundle/update.sh" "${BASH_SOURCE[0]}"; then
    echo "Continuing with the $version updater."
    exec env AUTOMATON_UPDATER_FROM_BUNDLE=1 bash "$bundle/update.sh" --version "$version" \
        --bundle "$working/automaton-server.tar.gz" --checksum "$working/automaton-server.tar.gz.sha256" \
        --install-dir "$install_dir" --config-dir "$config_dir" --backup-dir "$backup_root" --keep-backups "$keep_backups"
fi
# Nightly backups are on by default. Also runs when the release is already installed, so servers whose
# previous updater predates backups get them by running update.sh again. Failures only warn.
ensure_backups() {
    local script
    for script in backup.sh restore.sh; do
        if [[ -f "$bundle/$script" ]]; then install -m 0700 "$bundle/$script" "$install_dir/$script"; fi
    done
    [[ -f "$install_dir/backup.sh" ]] || return 0
    if bash "$install_dir/backup.sh" --install-dir "$install_dir" --install-timer \
        && { [[ -n "$(bash "$install_dir/backup.sh" --install-dir "$install_dir" --list)" ]] || bash "$install_dir/backup.sh" --install-dir "$install_dir"; }; then
        return 0
    fi
    echo "Warning: nightly backups could not be enabled. Run: sudo bash $install_dir/backup.sh --install-timer" >&2
}
if cmp -s "$bundle/release.env" "$install_dir/release.env"; then
    echo 'This release is already installed.'
    ensure_backups
    exit 0
fi
if [[ -f "$install_dir/version" ]]; then
    current_version=$(cat "$install_dir/version")
    [[ "$current_version" =~ ^server-v[0-9]+\.[0-9]+\.[0-9]+([.-][a-zA-Z0-9.-]+)?$ ]] || { echo 'Installed version marker is invalid.' >&2; exit 1; }
    dpkg --compare-versions "${version#server-v}" gt "${current_version#server-v}" || { echo 'Choose a newer server release.' >&2; exit 1; }
fi
python3 - "$install_dir/release.env" "$bundle/release.env" <<'PY'
import re, sys
def read(path):
    entries = dict(line.strip().split('=', 1) for line in open(path) if line.strip())
    required = {'API_IMAGE', 'WORKER_IMAGE', 'STUDIO_IMAGE', 'MYSQL_IMAGE', 'RABBITMQ_IMAGE', 'TRAEFIK_IMAGE', 'RUNNER_IMAGE'}
    # STORE_IMAGE was added after the first releases; older installed manifests do not have it.
    if set(entries) not in (required, required | {'STORE_IMAGE'}) or any(not re.fullmatch(r'[a-zA-Z0-9_./:-]+@sha256:[a-f0-9]{64}', value) for value in entries.values()):
        raise SystemExit('Invalid release image manifest.')
    return entries
old, new = read(sys.argv[1]), read(sys.argv[2])
if not set(old) <= set(new):
    raise SystemExit('This release removes an image. A reviewed migration is required.')
for name in ('MYSQL_IMAGE', 'RABBITMQ_IMAGE', 'TRAEFIK_IMAGE'):
    if old[name] != new[name]:
        raise SystemExit(f'{name} changed. A reviewed infrastructure migration is required.')
PY

old_compose=(docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml")
new_compose=(docker compose --project-directory "$install_dir" --env-file "$bundle/release.env" --env-file "$install_dir/.env" -f "$bundle/compose.yaml")
# Services owned by the release, without operator override additions such as sidecars.
old_release_services=$("${old_compose[@]}" config --services)
new_release_services=$("${new_compose[@]}" config --services)
override="$install_dir/compose.override.yaml"
if [[ -e "$override" || -L "$override" ]]; then
    [[ -f "$override" && ! -L "$override" ]] || { echo 'The installation Compose override must be a regular, non-symlink file.' >&2; exit 1; }
    old_compose+=(-f "$override")
    new_compose+=(-f "$override")
fi
"${old_compose[@]}" config --format json > "$working/old-compose.json"
"${new_compose[@]}" config --format json > "$working/new-compose.json"
# Releases may add or change application services. Infrastructure, persistent volumes,
# secrets and networks must stay identical; those changes need a reviewed migration.
python3 - "$working/old-compose.json" "$working/new-compose.json" <<'PY'
import json, sys
old, new = (json.load(open(path)) for path in sys.argv[1:3])
infrastructure = ('mysql', 'rabbitmq', 'traefik')
old_services, new_services = old.get('services', {}), new.get('services', {})
if not set(old_services) <= set(new_services):
    raise SystemExit('This release removes a service. A reviewed migration is required.')
for name in infrastructure:
    if old_services.get(name) != new_services.get(name):
        raise SystemExit(f'This release changes {name}. A reviewed infrastructure migration is required.')
for section in ('volumes', 'secrets', 'networks'):
    if old.get(section) != new.get(section):
        raise SystemExit(f'This release changes Compose {section}. A reviewed migration is required.')
PY
application_services=()
for service in $new_release_services; do
    [[ " mysql rabbitmq traefik " == *" $service "* ]] || application_services+=("$service")
done
for service in $old_release_services; do
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
if [[ " ${application_services[*]} " == *' store '* ]]; then
    [[ -f "$bundle/configure.py" ]] || { echo 'The release bundle is incomplete: configure.py is missing.' >&2; exit 1; }
    echo 'Adding app store configuration if missing.'
    if ! python3 "$bundle/configure.py" --config-dir "$config_dir" --ensure-store; then
        echo "App store configuration failed; services were not changed. Fix the reported configuration file, or restore it from $backup/config.tar.gz, then rerun the update." >&2
        exit 1
    fi
fi
old_runner_image=$(grep -m1 '^RUNNER_IMAGE=' "$install_dir/release.env" | cut -d= -f2-)
new_runner_image=$(grep -m1 '^RUNNER_IMAGE=' "$bundle/release.env" | cut -d= -f2-)
recreate=()
if [[ "$old_runner_image" != "$new_runner_image" ]]; then
    [[ -f "$bundle/configure.py" ]] || { echo 'The release bundle is incomplete: configure.py is missing.' >&2; exit 1; }
    # Only new Runner installations use this image; running Runners are not changed.
    if ! python3 "$bundle/configure.py" --config-dir "$config_dir" --update-runner-image "$old_runner_image" "$new_runner_image"; then
        echo "Runner image configuration failed; services were not changed. Restore it from $backup/config.tar.gz if needed, then rerun the update." >&2
        exit 1
    fi
    recreate=(--force-recreate)  # API reads the updated settings file only at startup.
fi
echo 'Backup verified. Pulling release images.'
"${new_compose[@]}" pull "${application_services[@]}"
echo "Updating ${application_services[*]}."
if ! "${new_compose[@]}" up -d --wait --wait-timeout 600 "${recreate[@]}" "${application_services[@]}"; then
    echo "Update did not reach healthy state. Previous manifest and recovery backup remain at $backup. Do not retry until the database and running services are assessed." >&2
    exit 1
fi
if ! curl --fail --silent --show-error --retry 12 --retry-delay 5 --retry-all-errors "https://studio.$domain/api/install/status" | python3 -c 'import json,sys; assert json.load(sys.stdin)["installationComplete"]' \
    || ! curl --fail --silent --show-error "https://studio.$domain/" >/dev/null; then
    echo "Public route validation failed. Previous manifest and recovery backup remain at $backup. Assess the services before recovery." >&2
    exit 1
fi
if [[ " ${application_services[*]} " == *' store '* ]] && ! curl --fail --silent --show-error --retry 12 --retry-delay 5 --retry-all-errors "https://store.$domain/" >/dev/null; then
    printf 'Warning: the app store did not answer at https://store.%s/. Point a DNS record for store.%s to this server, then restart Traefik so it requests the certificate; the rest of the update is complete.\n' "$domain" "$domain" >&2
fi
install -m 0600 "$bundle/release.env" "$install_dir/release.env"
install -m 0644 "$bundle/compose.yaml" "$install_dir/compose.yaml"
install -m 0644 "$bundle/version" "$install_dir/version"
install -m 0700 "$bundle/update.sh" "$install_dir/update.sh"
install -m 0700 "$bundle/install.sh" "$install_dir/install.sh"
echo "Updated to $version. Recovery backup: $backup"
ensure_backups
# Housekeeping runs only after success; a failure here never affects the completed update.
# Only directories this updater created (server-v<version>-<UTC time>-<random>) are rotated.
mapfile -t old_backups < <(find "$backup_root" -mindepth 1 -maxdepth 1 -type d -regextype posix-extended \
    -regex '.*/server-v[0-9][^/]*-[0-9]{8}T[0-9]{6}Z-[A-Za-z0-9]{6}' -printf '%T@ %p\n' | sort -rn | cut -d' ' -f2- | tail -n +"$((keep_backups + 1))")
for old_backup in "${old_backups[@]}"; do
    [[ "$old_backup" != "$backup" ]] || continue
    rm -rf -- "$old_backup" && echo "Removed old update backup: $old_backup" || echo "Warning: could not remove $old_backup" >&2
done
# Remove the previous release's application images unless a container still uses them.
python3 - "$backup/release.env" "$bundle/release.env" <<'PY' | while IFS= read -r image; do
import sys
def read(path):
    return dict(line.strip().split('=', 1) for line in open(path) if line.strip())
old, new = read(sys.argv[1]), read(sys.argv[2])
current = set(new.values())
for name in ('API_IMAGE', 'WORKER_IMAGE', 'STUDIO_IMAGE', 'STORE_IMAGE'):
    if old.get(name) and old[name] not in current:
        print(old[name])
PY
    if docker image inspect "$image" >/dev/null 2>&1; then
        docker image rm "$image" >/dev/null 2>&1 && echo "Removed previous image: $image" || echo "Kept previous image still in use: $image"
    fi
done
free_kb=$(df -Pk "$backup_root" | awk 'NR==2 {print $4}')
printf 'Free disk space: %s GiB.\n' "$((free_kb / 1048576))"
(( free_kb >= 10485760 )) || echo 'Warning: less than 10 GiB is free; the next update will refuse to start until space is freed.' >&2
echo 'Copy the backup to encrypted off-host storage; keep the database and Data Protection keys together.'
