#!/usr/bin/env bash
# Back up an Automaton server: a consistent database dump plus the API and Worker
# Data Protection keys that decrypt it. Runs nightly from automaton-backup.timer.
#
#   backup.sh                  take a backup now and rotate old ones
#   backup.sh --install-timer  install and start the nightly systemd timer
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
        --help) sed -n '2,8p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done
[[ "$keep" =~ ^[1-9][0-9]*$ ]] || { echo '--keep must be a positive number.' >&2; exit 2; }
[[ $EUID -eq 0 ]] || { echo 'Run as root (sudo).' >&2; exit 1; }
[[ -f "$install_dir/release.env" && -f "$install_dir/.env" ]] || { echo "No Automaton installation found in $install_dir." >&2; exit 1; }

backup_root="$install_dir/backups/nightly"
config_dir=$(grep -m1 '^CONFIG_DIR=' "$install_dir/.env" | cut -d= -f2-)
config_dir=${config_dir:-/etc/automaton}
status_dir="$config_dir/status"
name_pattern='^automaton-[0-9]{8}T[0-9]{6}Z$'

compose=(docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml")
[[ -f "$install_dir/compose.override.yaml" ]] && compose+=(-f "$install_dir/compose.override.yaml")

list_backups() {
    find "$backup_root" -mindepth 1 -maxdepth 1 -type d -regextype posix-extended -regex ".*/automaton-[0-9]{8}T[0-9]{6}Z" \
        -printf '%f\n' 2>/dev/null | sort -r
}

# Studio reads this file (public metadata only: names, times, sizes, last error).
write_status() {
    local ok="$1" error="$2"
    install -d -m 0755 "$status_dir"
    python3 - "$backup_root" "$status_dir/backup.json" "$ok" "$error" "$keep" <<'PY'
import json, os, sys, tempfile
from datetime import datetime, timezone
root, target, ok, error, keep = sys.argv[1:]
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
        size = sum(os.path.getsize(os.path.join(path, f)) for f in os.listdir(path))
        created = datetime.strptime(name[len('automaton-'):], '%Y%m%dT%H%M%SZ').replace(tzinfo=timezone.utc)
        backups.append({'name': name, 'createdAt': created.isoformat().replace('+00:00', 'Z'), 'sizeBytes': size})
now = datetime.now(timezone.utc).isoformat(timespec='seconds').replace('+00:00', 'Z')
status = {
    'lastAttemptAt': now,
    'lastSuccessAt': now if ok == 'true' else previous.get('lastSuccessAt'),
    'lastError': None if ok == 'true' else error,
    'keep': int(keep),
    'directory': root,
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
    chmod 0644 /etc/systemd/system/automaton-backup.service /etc/systemd/system/automaton-backup.timer
    systemctl daemon-reload
    systemctl enable --now automaton-backup.timer >/dev/null
    echo "Nightly backups enabled (automaton-backup.timer); keeping the newest $keep in $backup_root."
}

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
    "${compose[@]}" exec -T mysql sh -ec 'MYSQL_PWD=$(cat /run/secrets/mysql_root_password); export MYSQL_PWD; exec mysqldump --single-transaction --quick --routines --triggers --events --no-tablespaces -u root automaton' \
        < /dev/null | gzip -6 > "$working/database.sql.gz"
    gzip -t "$working/database.sql.gz"
    zcat "$working/database.sql.gz" | tail -n 1 | grep -q 'Dump completed' || { echo 'The database dump did not complete.' >&2; return 1; }
    docker cp "$api:/app/data-protection-keys/." - | gzip > "$working/api-keys.tar.gz"
    docker cp "$worker:/app/data-protection-keys/." - | gzip > "$working/worker-keys.tar.gz"
    for archive in api-keys worker-keys; do tar -tzf "$working/$archive.tar.gz" >/dev/null; done
    cp "$install_dir/version" "$working/version" 2>/dev/null || true
    (cd "$working" && sha256sum database.sql.gz api-keys.tar.gz worker-keys.tar.gz > SHA256SUMS && sha256sum -c --quiet SHA256SUMS)
    mv -- "$working" "$backup_root/$name"
    echo "Backup saved: $backup_root/$name ($(du -sh "$backup_root/$name" | cut -f1))"
    # Rotate only backups this script created.
    list_backups | tail -n +"$((keep + 1))" | while read -r old; do
        [[ "$old" =~ $name_pattern ]] && rm -rf -- "${backup_root:?}/$old" && echo "Removed old backup: $old"
    done
}

case "$mode" in
    timer) install_timer ;;
    list) list_backups ;;
    backup)
        exec 9>/run/lock/automaton-backup.lock
        flock -n 9 || { echo 'Another backup is running.' >&2; exit 1; }
        if take_backup; then
            write_status true ''
        else
            write_status false 'The last backup failed. Run: sudo journalctl -u automaton-backup.service'
            exit 1
        fi
        ;;
esac
