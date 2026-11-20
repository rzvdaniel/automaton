#!/usr/bin/env bash
# Restore an Automaton server from a backup made by backup.sh.
#
#   restore.sh automaton-YYYYMMDDTHHMMSSZ
#   restore.sh automaton-YYYYMMDDTHHMMSSZ --app <app id or Compose project>
#
# Replaces the whole database, the API/Worker Data Protection keys and the app data with the backup.
# Everything written after that backup is lost. A safety backup of the current state is
# taken first. API, Worker, Studio and the app store are stopped during the restore.
# With --app only that one app's data is restored (the database and the other apps are untouched).
set -euo pipefail
umask 077

install_dir=/opt/automaton-server
name=''
archive=''
key=''
confirm=''
app=''
while (($#)); do
    case "$1" in
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --confirm) confirm="${2:?Missing domain}"; shift 2 ;;
        --archive) archive="${2:?Missing archive file}"; shift 2 ;;
        --app) app="${2:?Missing app id or Compose project}"; shift 2 ;;
        --key) key="${2:?Missing age identity file}"; shift 2 ;;
        --help) sed -n '2,10p' "$0"; exit 0 ;;
        -*) echo "Unknown option: $1" >&2; exit 2 ;;
        *) name="$1"; shift ;;
    esac
done
[[ $EUID -eq 0 ]] || { echo 'Run as root (sudo).' >&2; exit 1; }
[[ -z "$archive" || -z "$name" ]] || { echo 'Choose either a backup name or --archive, not both.' >&2; exit 2; }
backup_root="$install_dir/backups/nightly"
install -d -m 0700 "$install_dir/backups"
if [[ -n "$archive" || -n "$key" ]]; then
    [[ -n "$archive" && -n "$key" ]] || { echo 'Specify both --archive and --key.' >&2; exit 2; }
    [[ -f "$archive" && -f "$key" ]] || { echo 'Archive or key file not found.' >&2; exit 1; }
    working=$(mktemp -d "$install_dir/backups/.restore.XXXXXX")
    trap 'rm -rf -- "$working"' EXIT
    age_image=$(grep -m1 '^AGE_IMAGE=' "$install_dir/release.env" | cut -d= -f2-)
    [[ "$age_image" =~ @sha256:[0-9a-f]{64}$ ]] || { echo 'The release manifest has no pinned age image.' >&2; exit 1; }
    docker run --rm -i -v "$key:/identity:ro" "$age_image" age --decrypt -i /identity < "$archive" > "$working/backup.tar"
    python3 - "$working/backup.tar" "$working" <<'PY'
import pathlib, re, sys, tarfile

def extract(archive, destination, members=None):
    # tarfile filter= exists only from Python 3.11.4; members were validated above (regular files and directories only).
    try:
        archive.extractall(destination, members=members, filter='data')
    except TypeError:
        for member in (members if members is not None else archive.getmembers()):
            member.mode &= 0o755
            member.uid = member.gid = 0
            member.uname = member.gname = ''
        archive.extractall(destination, members=members)
archive, destination = sys.argv[1:]
with tarfile.open(archive, 'r:') as source:
    members = source.getmembers()
    roots = set()
    for member in members:
        path = pathlib.PurePosixPath(member.name)
        if path.is_absolute() or '..' in path.parts or not path.parts:
            raise SystemExit('Archive contains an unsafe path.')
        if not re.fullmatch(r'automaton-[0-9]{8}T[0-9]{6}Z', path.parts[0]):
            raise SystemExit('Archive contains an invalid backup path.')
        roots.add(path.parts[0])
        if not (member.isdir() or member.isfile()) or getattr(member, 'sparse', None):
            raise SystemExit('Archive contains an unsupported file type.')
    if len(roots) != 1:
        raise SystemExit('Archive must contain exactly one backup directory.')
    extract(source, destination, members)
PY
    rm "$working/backup.tar"
    mapfile -t restored < <(find "$working" -mindepth 1 -maxdepth 1 -type d -name 'automaton-*' -printf '%f\n')
    [[ ${#restored[@]} == 1 ]] || { echo 'Archive must contain exactly one backup directory.' >&2; exit 1; }
    name=${restored[0]}
    [[ "$name" =~ ^automaton-[0-9]{8}T[0-9]{6}Z$ ]] || { echo 'Archive contains an invalid backup name.' >&2; exit 1; }
    backup="$working/$name"
else
if [[ -z "$name" ]]; then
    echo 'Choose a backup to restore:' >&2
    bash "$install_dir/backup.sh" --install-dir "$install_dir" --list >&2
    echo "Then run: sudo bash $install_dir/restore.sh <backup name>" >&2
    exit 2
fi
[[ "$name" =~ ^automaton-[0-9]{8}T[0-9]{6}Z$ ]] || { echo "Not a backup name: $name" >&2; exit 2; }
backup="$backup_root/$name"
[[ -d "$backup" ]] || { echo "Backup not found: $backup" >&2; exit 1; }
fi
[[ -f "$backup/database.dump" ]] || { echo 'This is not a PostgreSQL backup (no database.dump); nothing was restored.' >&2; exit 1; }
(cd "$backup" && sha256sum -c --quiet SHA256SUMS) || { echo 'The backup failed its checksum verification; it was not restored.' >&2; exit 1; }

# app-data:begin
# restore_app_data <only this app id/project, or empty for all> <helper image> <backup directory>
restore_app_data() {
    local only="$1" helper="$2" backup="$3" manifest="$3/app-data/manifest.tsv" kind key volume sub file label found=0 ids
    [[ -f "$manifest" ]] || { echo 'This backup has no app data (it was taken before app data was included).'; return 0; }
    while IFS=$'\t' read -r kind key volume sub file; do
        [[ -z "$only" || "$key" == "$only" ]] || continue
        found=1
        if ! docker volume inspect "$volume" >/dev/null 2>&1; then
            if [[ "$kind" == compose ]]; then
                docker volume create --label "com.docker.compose.project=$key" --label "com.docker.compose.volume=$sub" "$volume" >/dev/null
                sub=.
            else
                echo "Skipping app $key: volume $volume does not exist on this host (install the Runner first)." >&2
                continue
            fi
        fi
        if [[ "$kind" == web ]]; then label="com.automaton.app-id=$key"; else label="com.docker.compose.project=$key"; fi
        mapfile -t ids < <(docker ps -q --filter "label=$label")
        if ((${#ids[@]})); then docker stop "${ids[@]}" >/dev/null; fi
        echo "Restoring data of $kind app $key ($volume)..."
        gzip -dc "$backup/app-data/$file" | docker run --rm -i --user 0:0 --entrypoint sh -v "$volume:/v" "$helper" \
            -ec 'd="/v/$1"; mkdir -p "$d"; find "$d" -mindepth 1 -delete; tar -xpf - -C "$d"' _ "$sub"
        if ((${#ids[@]})); then docker start "${ids[@]}" >/dev/null; fi
    done < "$manifest"
    if ((found == 0)); then
        echo "This backup has no data for $only. It has:" >&2
        cut -f2 "$manifest" | sort -u >&2
        return 1
    fi
}
# app-data:end

config_dir=$(grep -m1 '^CONFIG_DIR=' "$install_dir/.env" | cut -d= -f2-)
domain=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["domain"])' "${config_dir:-/etc/automaton}/installation.json")
compose=(docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml")
[[ -f "$install_dir/compose.override.yaml" ]] && compose+=(-f "$install_dir/compose.override.yaml")

backup_version=$(cat "$backup/version" 2>/dev/null || echo unknown)
current_version=$(cat "$install_dir/version" 2>/dev/null || echo unknown)
echo "Restoring $name (server $backup_version) on $domain (server $current_version)."
if [[ -n "$app" ]]; then
    echo "This replaces the data of $app only. Everything that app wrote after the backup is lost."
else
    echo 'This replaces the database, Data Protection keys and app data. Everything written after the backup is lost.'
fi
if [[ "$backup_version" != "$current_version" ]]; then
    echo "Warning: the backup was taken on $backup_version. The API applies newer database migrations on start; restoring onto an older server version is not supported." >&2
fi
if [[ -z "$confirm" ]]; then
    [[ -t 0 ]] || { echo "Confirm non-interactively with --confirm $domain" >&2; exit 2; }
    read -r -p "Type the server domain ($domain) to continue: " confirm
fi
[[ "$confirm" == "$domain" ]] || { echo 'Confirmation did not match; nothing was changed.' >&2; exit 1; }

if [[ -n "$app" ]]; then
    [[ -f "$backup/app-data/manifest.tsv" ]] && cut -f2 "$backup/app-data/manifest.tsv" | grep -Fxq -- "$app" \
        || { echo "This backup has no data for $app. Nothing was changed." >&2; exit 1; }
fi
echo 'Taking a safety backup of the current state...'
bash "$install_dir/backup.sh" --install-dir "$install_dir" --keep 1000

if [[ -n "$app" ]]; then
    restore_app_data "$app" "$(docker inspect --format '{{.Config.Image}}' "$("${compose[@]}" ps -aq api)")" "$backup"
    echo "Restored the data of $app from $name. The pre-restore state is kept as the newest backup in $backup_root."
    exit 0
fi

applications=(api worker studio store)
existing=$("${compose[@]}" config --services)
services=()
for service in "${applications[@]}"; do grep -Fxq "$service" <<<"$existing" && services+=("$service"); done
api=$("${compose[@]}" ps -aq api)
worker=$("${compose[@]}" ps -aq worker)
echo "Stopping ${services[*]}..."
"${compose[@]}" stop "${services[@]}"

echo 'Restoring the database...'
# Recreate the database, so tables added after the backup do not survive and the restored schema
# matches the backup exactly. The dump recreates the citext extension and the application schemas.
"${compose[@]}" exec -T postgres psql -U automaton -d postgres -v ON_ERROR_STOP=1 \
    -c 'DROP DATABASE IF EXISTS automaton WITH (FORCE)' -c 'CREATE DATABASE automaton' < /dev/null
"${compose[@]}" exec -T postgres pg_restore -U automaton -d automaton --exit-on-error --no-owner < "$backup/database.dump"

echo 'Restoring Data Protection keys...'
restore_keys() {
    local container="$1" archive="$2" helper
    helper=$(docker inspect --format '{{.Config.Image}}' "$container")
    local volume
    volume=$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/app/data-protection-keys"}}{{.Name}}{{end}}{{end}}' "$container")
    [[ -n "$volume" ]] || { echo "No Data Protection key volume on $container." >&2; return 1; }
    gzip -dc "$archive" | docker run --rm -i --user 0:0 --entrypoint sh -v "$volume:/keys" "$helper" \
        -ec 'o=$(stat -c %u:%g /keys); find /keys -mindepth 1 -delete; tar -xf - -C /keys; chown -R "$o" /keys'
}
restore_keys "$api" "$backup/api-keys.tar.gz"
restore_keys "$worker" "$backup/worker-keys.tar.gz"

restore_app_data '' "$(docker inspect --format '{{.Config.Image}}' "$api")" "$backup"

echo "Starting ${services[*]}..."
"${compose[@]}" up -d --wait --wait-timeout 600 "${services[@]}"
curl --fail --silent --show-error --retry 12 --retry-delay 5 --retry-all-errors "https://studio.$domain/api/install/status" >/dev/null
echo "Restored $name. The pre-restore state is kept as the newest backup in $backup_root."
