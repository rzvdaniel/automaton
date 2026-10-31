#!/usr/bin/env bash
# Restore an Automaton server from a backup made by backup.sh.
#
#   restore.sh automaton-YYYYMMDDTHHMMSSZ
#
# Replaces the whole database and the API/Worker Data Protection keys with the backup.
# Everything written after that backup is lost. A safety backup of the current state is
# taken first. API, Worker, Studio and the app store are stopped during the restore.
set -euo pipefail
umask 077

install_dir=/opt/automaton-server
name=''
confirm=''
while (($#)); do
    case "$1" in
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --confirm) confirm="${2:?Missing domain}"; shift 2 ;;
        --help) sed -n '2,8p' "$0"; exit 0 ;;
        -*) echo "Unknown option: $1" >&2; exit 2 ;;
        *) name="$1"; shift ;;
    esac
done
[[ $EUID -eq 0 ]] || { echo 'Run as root (sudo).' >&2; exit 1; }
backup_root="$install_dir/backups/nightly"
if [[ -z "$name" ]]; then
    echo 'Choose a backup to restore:' >&2
    bash "$install_dir/backup.sh" --install-dir "$install_dir" --list >&2
    echo "Then run: sudo bash $install_dir/restore.sh <backup name>" >&2
    exit 2
fi
[[ "$name" =~ ^automaton-[0-9]{8}T[0-9]{6}Z$ ]] || { echo "Not a backup name: $name" >&2; exit 2; }
backup="$backup_root/$name"
[[ -d "$backup" ]] || { echo "Backup not found: $backup" >&2; exit 1; }
(cd "$backup" && sha256sum -c --quiet SHA256SUMS) || { echo 'The backup failed its checksum verification; it was not restored.' >&2; exit 1; }

config_dir=$(grep -m1 '^CONFIG_DIR=' "$install_dir/.env" | cut -d= -f2-)
domain=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["domain"])' "${config_dir:-/etc/automaton}/installation.json")
compose=(docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml")
[[ -f "$install_dir/compose.override.yaml" ]] && compose+=(-f "$install_dir/compose.override.yaml")

backup_version=$(cat "$backup/version" 2>/dev/null || echo unknown)
current_version=$(cat "$install_dir/version" 2>/dev/null || echo unknown)
echo "Restoring $name (server $backup_version) on $domain (server $current_version)."
echo 'This replaces the database and Data Protection keys. Everything written after the backup is lost.'
if [[ "$backup_version" != "$current_version" ]]; then
    echo "Warning: the backup was taken on $backup_version. The API applies newer database migrations on start; restoring onto an older server version is not supported." >&2
fi
if [[ -z "$confirm" ]]; then
    [[ -t 0 ]] || { echo "Confirm non-interactively with --confirm $domain" >&2; exit 2; }
    read -r -p "Type the server domain ($domain) to continue: " confirm
fi
[[ "$confirm" == "$domain" ]] || { echo 'Confirmation did not match; nothing was changed.' >&2; exit 1; }

echo 'Taking a safety backup of the current state...'
bash "$install_dir/backup.sh" --install-dir "$install_dir" --keep 1000

applications=(api worker studio store)
existing=$("${compose[@]}" config --services)
services=()
for service in "${applications[@]}"; do grep -Fxq "$service" <<<"$existing" && services+=("$service"); done
api=$("${compose[@]}" ps -aq api)
worker=$("${compose[@]}" ps -aq worker)
echo "Stopping ${services[*]}..."
"${compose[@]}" stop "${services[@]}"

echo 'Restoring the database...'
# Recreate the database with its existing character set and collation, so tables added after the
# backup do not survive and the restored schema matches the backup exactly.
"${compose[@]}" exec -T mysql sh -ec 'MYSQL_PWD=$(cat /run/secrets/mysql_root_password); export MYSQL_PWD
set -- $(mysql -N -u root -e "SELECT DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = '"'"'automaton'"'"'")
mysql -u root -e "DROP DATABASE IF EXISTS automaton; CREATE DATABASE automaton CHARACTER SET ${1:-utf8mb4} COLLATE ${2:-utf8mb4_0900_ai_ci};"' < /dev/null
zcat "$backup/database.sql.gz" | "${compose[@]}" exec -T mysql sh -ec 'MYSQL_PWD=$(cat /run/secrets/mysql_root_password); export MYSQL_PWD; exec mysql -u root automaton'

echo 'Restoring Data Protection keys...'
restore_keys() {
    local container="$1" archive="$2" helper
    helper=$(docker inspect --format '{{.Config.Image}}' "$container")
    local volume
    volume=$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/app/data-protection-keys"}}{{.Name}}{{end}}{{end}}' "$container")
    [[ -n "$volume" ]] || { echo "No Data Protection key volume on $container." >&2; return 1; }
    gzip -dc "$archive" | docker run --rm -i --user 0:0 --entrypoint sh -v "$volume:/keys" "$helper" \
        -ec 'find /keys -mindepth 1 -delete; tar -xf - -C /keys; chown -R --reference=/keys /keys'
}
restore_keys "$api" "$backup/api-keys.tar.gz"
restore_keys "$worker" "$backup/worker-keys.tar.gz"

echo "Starting ${services[*]}..."
"${compose[@]}" up -d --wait --wait-timeout 600 "${services[@]}"
curl --fail --silent --show-error --retry 12 --retry-delay 5 --retry-all-errors "https://studio.$domain/api/install/status" >/dev/null
echo "Restored $name. The pre-restore state is kept as the newest backup in $backup_root."
