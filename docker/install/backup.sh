#!/usr/bin/env bash
# Back up an Automaton server: a consistent database dump, the API and Worker Data Protection
# keys that decrypt it, and the data of every app this host runs (web app data directories and
# Compose volumes). Runs nightly from automaton-backup.timer.
#
#   backup.sh                  take a backup now and rotate old ones
#   backup.sh --install-timer  install and start the nightly systemd timer, and the unit that starts a backup
#                              requested from Studio or the CLI (automaton-backup-request.path)
#   backup.sh --list           list backups
set -euo pipefail
umask 077

install_dir=/opt/automaton-server
keep=7
mode=backup
while (($#)); do
    case "$1" in
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --keep) keep="${2:?Missing number of backups to keep}"; shift 2 ;;
        --install-timer) mode=timer; shift ;;
        --list) mode=list; shift ;;
        --help) sed -n '2,10p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done
[[ "$keep" =~ ^[1-9][0-9]*$ ]] || { echo '--keep must be a positive number.' >&2; exit 2; }
[[ $EUID -eq 0 ]] || { echo 'Run as root (sudo).' >&2; exit 1; }
[[ -f "$install_dir/release.env" && -f "$install_dir/.env" ]] || { echo "No Automaton installation found in $install_dir." >&2; exit 1; }

backup_root="$install_dir/backups/nightly"
config_dir=$(grep -m1 '^CONFIG_DIR=' "$install_dir/.env" | cut -d= -f2-)
config_dir=${config_dir:-/etc/automaton}
if grep -q '^AGE_IMAGE=' "$install_dir/release.env"; then
    age_image=$(grep -m1 '^AGE_IMAGE=' "$install_dir/release.env" | cut -d= -f2-)
    [[ -n "$age_image" ]] || { echo 'The release manifest has an empty age image.' >&2; exit 1; }
else
    age_image=''
fi
recipient_file="$config_dir/backup-recipient"
identity_file="$config_dir/secrets/backup-key"
status_dir="$config_dir/status"
request_dir=${AUTOMATON_BACKUP_REQUEST_DIR:-/var/lib/automaton/backup/requests}
name_pattern='^automaton-[0-9]{8}T[0-9]{6}Z$'

compose=(docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml")
if [[ -f "$install_dir/compose.override.yaml" ]]; then
    compose+=(-f "$install_dir/compose.override.yaml")
fi

list_backups() {
    [[ -d "$backup_root" ]] || return 0
    find "$backup_root" -mindepth 1 -maxdepth 1 -type d -regextype posix-extended -regex ".*/automaton-[0-9]{8}T[0-9]{6}Z" \
        -printf '%f\n' 2>/dev/null | sort -r
}

# Studio reads this file (public metadata only: names, times, sizes, last error).
write_status() {
    local ok="$1" error="$2"
    install -d -m 0755 "$status_dir"
    # When the timer fires next (empty if it is not installed); systemd prints it in the server's time zone.
    local next_run
    local next_raw
    next_raw=$(systemctl show automaton-backup.timer -p NextElapseUSecRealtime --value 2>/dev/null || true)
    next_run=''
    if [[ -n "$next_raw" && "$next_raw" != n/a ]]; then
        next_run=$(date -u -d "$next_raw" +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || true)
    fi
    python3 - "$backup_root" "$status_dir/backup.json" "$ok" "$error" "$keep" "$recipient_file" "$next_run" <<'PY'
import json, os, shlex, sys, tempfile
from datetime import datetime, timezone
root, target, ok, error, keep, recipient_file, next_run = sys.argv[1:]
previous = {}
try:
    previous = json.load(open(target))
except (OSError, ValueError):
    pass
backups = []
if os.path.isdir(root):
    for name in sorted(os.listdir(root), reverse=True):
        path = os.path.join(root, name)
        if not (name.startswith('automaton-') and os.path.isdir(path) and os.path.isfile(os.path.join(path, 'SHA256SUMS'))):
            continue
        size = sum(os.path.getsize(os.path.join(d, f)) for d, _, files in os.walk(path) for f in files)
        created = datetime.strptime(name[len('automaton-'):], '%Y%m%dT%H%M%SZ').replace(tzinfo=timezone.utc)
        archive = os.path.join(root, name + '.tar.age')
        archive_size = None
        archive_hash = None
        archive_mtime_ns = None
        if os.path.isfile(archive):
            stat = os.stat(archive)
            archive_size = stat.st_size
            archive_mtime_ns = stat.st_mtime_ns
            cached = next((item for item in previous.get('backups', []) if item.get('name') == name), {})
            if cached.get('archiveSizeBytes') == archive_size and cached.get('archiveMtimeNs') == archive_mtime_ns:
                archive_hash = cached.get('archiveSha256')
            else:
                import hashlib
                digest = hashlib.sha256()
                with open(archive, 'rb') as source:
                    for block in iter(lambda: source.read(1024 * 1024), b''):
                        digest.update(block)
                archive_hash = digest.hexdigest()
        backups.append({'name': name, 'createdAt': created.isoformat().replace('+00:00', 'Z'), 'sizeBytes': size,
                        'archiveSizeBytes': archive_size, 'archiveSha256': archive_hash, 'archiveMtimeNs': archive_mtime_ns})
now = datetime.now(timezone.utc).isoformat(timespec='seconds').replace('+00:00', 'Z')
status = {
    'lastAttemptAt': now,
    'lastSuccessAt': now if ok == 'true' else previous.get('lastSuccessAt'),
    'lastError': None if ok == 'true' else error,
    'nextRunAt': next_run or None,
    'keep': int(keep),
    'directory': root,
    'backupRecipient': open(recipient_file).read().strip() if os.path.isfile(recipient_file) else None,
    'backupKeyCommand': 'sudo cat ' + shlex.quote(os.path.join(os.path.dirname(recipient_file), 'secrets', 'backup-key')),
    'backups': backups,
}
fd, temporary = tempfile.mkstemp(dir=os.path.dirname(target))
with os.fdopen(fd, 'w') as stream:
    json.dump(status, stream, indent=2)
os.chmod(temporary, 0o644)
os.replace(temporary, target)
PY
}

install_timer() {
    cat > /etc/systemd/system/automaton-backup.service <<UNIT
[Unit]
Description=Automaton nightly database and key backup
After=docker.service
Requires=docker.service

[Service]
Type=oneshot
ExecStart=/usr/bin/env bash $install_dir/backup.sh --install-dir $install_dir --keep $keep
UNIT
    cat > /etc/systemd/system/automaton-backup.timer <<'UNIT'
[Unit]
Description=Run the Automaton backup nightly

[Timer]
OnCalendar=*-*-* 03:00:00
RandomizedDelaySec=1h
Persistent=true

[Install]
WantedBy=timers.target
UNIT
    # The API container never gets root: it only drops request.json here, and this path unit starts the same service.
    install -d -m 0755 "$(dirname "$request_dir")"
    install -d -m 0700 "$request_dir"
    cat > /etc/systemd/system/automaton-backup-request.service <<UNIT
[Unit]
Description=Start an Automaton backup requested from Studio or the CLI
After=docker.service
Wants=docker.service
StartLimitIntervalSec=0

[Service]
Type=oneshot
ExecStartPre=/bin/rm -rf -- $request_dir/request.json
ExecStart=/usr/bin/systemctl start automaton-backup.service
UNIT
    cat > /etc/systemd/system/automaton-backup-request.path <<UNIT
[Unit]
Description=Watch for Automaton backup requests

[Path]
PathExists=$request_dir/request.json
Unit=automaton-backup-request.service
TriggerLimitIntervalSec=0

[Install]
WantedBy=multi-user.target
UNIT
    chmod 0644 /etc/systemd/system/automaton-backup.service /etc/systemd/system/automaton-backup.timer \
        /etc/systemd/system/automaton-backup-request.service /etc/systemd/system/automaton-backup-request.path
    systemctl daemon-reload
    systemctl enable --now automaton-backup.timer automaton-backup-request.path >/dev/null
    echo "Nightly backups enabled (automaton-backup.timer); keeping the newest $keep in $backup_root."
}

initialize_backup_key() {
    docker pull --quiet "$age_image" >/dev/null 2>&1 || { echo 'Could not pull the pinned age image.' >&2; return 1; }
    install -d -m 0700 "$config_dir/secrets"
    if [[ ! -e "$recipient_file" && ! -e "$identity_file" ]]; then
        local generated recipient temporary_identity="$config_dir/secrets/.backup-key.partial"
        generated=$(docker run --rm -v "$config_dir/secrets:/keys" "$age_image" age-keygen -o /keys/.backup-key.partial 2>&1 >/dev/null)
        recipient=$(sed -n 's/^Public key: //p' <<< "$generated")
        [[ "$recipient" =~ ^age1[0-9a-z]+$ ]] || { rm -f -- "$temporary_identity"; echo 'Could not initialize the encrypted backup key.' >&2; return 1; }
        chmod 0600 "$temporary_identity"
        mv -- "$temporary_identity" "$identity_file"
        printf '%s\n' "$recipient" > "$recipient_file"
        chmod 0644 "$recipient_file"
    elif [[ -s "$identity_file" && ! -e "$recipient_file" ]]; then
        local recipient
        recipient=$(docker run --rm -v "$config_dir/secrets:/keys:ro" "$age_image" age-keygen -y /keys/backup-key)
        [[ "$recipient" =~ ^age1[0-9a-z]+$ ]] || { echo 'Could not read the encrypted backup recipient.' >&2; return 1; }
        printf '%s\n' "$recipient" > "$recipient_file"
        chmod 0644 "$recipient_file"
    fi
    [[ -s "$recipient_file" && -s "$identity_file" && ! -L "$recipient_file" && ! -L "$identity_file" ]] || {
        echo 'The encrypted backup key pair is incomplete.' >&2
        return 1
    }
}

# app-data:begin
# Apps keep files outside the database: web apps and Actions in workspace/data/<appId> of each Runner
# workspace volume on this host (declared storage mounts live inside it), Compose apps in the named
# volumes of their Compose project. Each is archived on its own so one app can be restored alone.
# Running containers of the app are paused (frozen, not stopped) while its data is copied.
app_guid='^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
paused_containers=()
resume_paused() {
    if ((${#paused_containers[@]})); then docker unpause "${paused_containers[@]}" >/dev/null 2>&1 || true; fi
    paused_containers=()
}
# archive_volume <label filter of containers to pause> <volume> <subdirectory> <output file> <helper image>
archive_volume() {
    local filter="$1" volume="$2" subdir="$3" out="$4" helper="$5" status=0
    mapfile -t paused_containers < <(docker ps -q --filter status=running --filter "label=$filter")
    if ((${#paused_containers[@]})); then docker pause "${paused_containers[@]}" >/dev/null || status=1; fi
    if ((status == 0)); then
        docker run --rm --user 0:0 --entrypoint sh -v "$volume:/v:ro" "$helper" -ec 'tar -C "/v/$1" -cf - .' _ "$subdir" \
            < /dev/null | gzip > "$out" || status=1
    fi
    resume_paused
    ((status == 0)) && tar -tzf "$out" > /dev/null
}
# backup_app_data <backup directory> <helper image with sh and tar> <server compose project>
backup_app_data() {
    local working="$1" helper="$2" server_project="$3" count=0 volume app project short
    mkdir -m 0700 "$working/app-data"
    local manifest="$working/app-data/manifest.tsv"
    : > "$manifest"
    while read -r volume; do
        [[ -n "$volume" ]] || continue
        while read -r app; do
            [[ "$app" =~ $app_guid ]] || continue
            count=$((count + 1))
            archive_volume "com.automaton.app-id=$app" "$volume" "workspace/data/$app" "$working/app-data/$count.tar.gz" "$helper" \
                || { echo "Could not back up the data of app $app." >&2; return 1; }
            printf 'web\t%s\t%s\t%s\t%s\n' "$app" "$volume" "workspace/data/$app" "$count.tar.gz" >> "$manifest"
        done < <(docker run --rm --user 0:0 --entrypoint sh -v "$volume:/v:ro" "$helper" -c 'ls -1 /v/workspace/data 2>/dev/null || true' < /dev/null)
    done < <(docker volume ls -q --filter label=automaton.resource=machine-workspace-volume)
    while read -r volume project short; do
        [[ -n "$volume" && -n "$project" && "$project" != "$server_project" ]] || continue
        count=$((count + 1))
        archive_volume "com.docker.compose.project=$project" "$volume" . "$working/app-data/$count.tar.gz" "$helper" \
            || { echo "Could not back up volume $volume of Compose app $project." >&2; return 1; }
        printf 'compose\t%s\t%s\t%s\t%s\n' "$project" "$volume" "$short" "$count.tar.gz" >> "$manifest"
    done < <(docker volume ls --filter label=com.docker.compose.project --format '{{.Name}} {{.Label "com.docker.compose.project"}} {{.Label "com.docker.compose.volume"}}')
    echo "Backed up the data of $count app volume(s)."
}
# app-data:end

take_backup() {
    install -d -m 0700 "$install_dir/backups" "$backup_root"
    local name="automaton-$(date -u +%Y%m%dT%H%M%SZ)"
    local working="$backup_root/.$name.partial"
    rm -rf -- "$working"
    mkdir -m 0700 "$working"
    local api worker
    api=$("${compose[@]}" ps -q api)
    worker=$("${compose[@]}" ps -q worker)
    [[ -n "$api" && -n "$worker" ]] || { echo 'API and Worker containers must exist to back up their Data Protection keys.' >&2; return 1; }
    "${compose[@]}" exec -T postgres pg_dump -U automaton -d automaton --format=custom --compress=6 < /dev/null > "$working/database.dump"
    # pg_restore --list fails on a truncated or corrupt custom-format archive.
    "${compose[@]}" exec -T postgres pg_restore --list < "$working/database.dump" > /dev/null || { echo 'The database dump did not complete.' >&2; return 1; }
    docker cp "$api:/app/data-protection-keys/." - | gzip > "$working/api-keys.tar.gz"
    docker cp "$worker:/app/data-protection-keys/." - | gzip > "$working/worker-keys.tar.gz"
    for archive in api-keys worker-keys; do tar -tzf "$working/$archive.tar.gz" >/dev/null; done
    local server_project helper
    server_project=$(docker inspect --format '{{index .Config.Labels "com.docker.compose.project"}}' "$api")
    helper=$(docker inspect --format '{{.Config.Image}}' "$api")
    backup_app_data "$working" "$helper" "$server_project"
    cp "$install_dir/version" "$working/version" 2>/dev/null || true
    (cd "$working" && sha256sum database.dump api-keys.tar.gz worker-keys.tar.gz $(find app-data -type f | sort) > SHA256SUMS && sha256sum -c --quiet SHA256SUMS)
    mv -- "$working" "$backup_root/$name"
    if [[ -n "$age_image" ]]; then
        [[ "$age_image" =~ @sha256:[0-9a-f]{64}$ ]] || { echo 'The release manifest has an invalid pinned age image.' >&2; return 1; }
        initialize_backup_key
        local archive="$backup_root/$name.tar.age" temporary="$backup_root/.$name.tar.age.partial"
        if ! tar -C "$backup_root" -cf - "$name" | docker run --rm -i "$age_image" age -r "$(cat "$recipient_file")" > "$temporary"; then
            rm -f -- "$temporary"
            return 1
        fi
        chmod 0600 "$temporary"
        mv -- "$temporary" "$archive"
    else
        echo 'Warning: the release has no pinned age image; this backup is not encrypted.' >&2
    fi
    echo "Backup saved: $backup_root/$name ($(du -sh "$backup_root/$name" | cut -f1))"
    # Rotate only backups this script created.
    list_backups | tail -n +"$((keep + 1))" | while read -r old; do
        [[ "$old" =~ $name_pattern ]] && rm -rf -- "${backup_root:?}/$old" "${backup_root:?}/$old.tar.age" && echo "Removed old backup: $old"
    done
}

case "$mode" in
    timer) install_timer ;;
    list) list_backups ;;
    backup)
        exec 9>"${AUTOMATON_BACKUP_LOCK_FILE:-/run/lock/automaton-backup.lock}"
        flock -n 9 || { echo 'Another backup is running.' >&2; exit 1; }
        # Lets the API refuse a server restart while this backup runs (the status directory is mounted read-only into it).
        install -d -m 0755 "$status_dir" && : > "$status_dir/backup.running"
        trap 'resume_paused; rm -f -- "$status_dir/backup.running"' EXIT
        if take_backup; then
            write_status true ''
        else
            write_status false 'The last backup failed. Run: sudo journalctl -u automaton-backup.service'
            exit 1
        fi
        ;;
esac
