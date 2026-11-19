#!/usr/bin/env bash
# Restart the host that an administrator requested from Studio, the CLI or an approved app.
#
# The API container never gets root. It only writes /var/lib/automaton/restart/requests/request.json;
# automaton-restart.path notices the file and automaton-restart.service (root) runs this script, which validates
# the request, refuses while a backup or a server update is running, and then reboots now or at the requested time.
#
#   restart-from-request.sh --install-units   create the restart directories and systemd units (installer/updater)
#   restart-from-request.sh                   process one pending request (started by automaton-restart.service)
#   restart-from-request.sh --reboot          reboot now if nothing is running (started by the scheduled timer)
#
# Only requestedBy and an optional restartAtUtc are taken from the request; nothing in the file is ever executed or
# interpolated into a command. The request is always removed before this script returns, so the path unit never loops.
set -uo pipefail
umask 077

install_dir=/opt/automaton-server
restart_dir=${AUTOMATON_RESTART_DIR:-/var/lib/automaton/restart}
update_dir=${AUTOMATON_UPDATE_DIR:-/var/lib/automaton/update}
os_update_dir=${AUTOMATON_OS_UPDATE_DIR:-/var/lib/automaton/os-update}
systemd_dir=${AUTOMATON_SYSTEMD_DIR:-/etc/systemd/system}
mode=run
while (($#)); do
    case "$1" in
        --install-units) mode=units; shift ;;
        --reboot) mode=reboot; shift ;;
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --help) echo 'Usage: restart-from-request.sh [--install-units|--reboot] [--install-dir PATH]'; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done
[[ "$install_dir" =~ ^/[a-zA-Z0-9_./-]+$ && "$install_dir" != / ]] || { echo 'Use an absolute installation directory.' >&2; exit 1; }
# AUTOMATON_RESTART_TEST lets the script tests run without root and without rebooting. systemd starts this script
# with a clean environment, so the API (which only writes request.json) can never set it.
if [[ -z "${AUTOMATON_RESTART_TEST:-}" && $EUID != 0 ]]; then echo 'Run with sudo.' >&2; exit 1; fi

requests="$restart_dir/requests"
status_dir="$restart_dir/status"
request="$requests/request.json"
backup_lock=${AUTOMATON_BACKUP_LOCK_FILE:-/run/lock/automaton-backup.lock}
export REQUESTED_BY='' REQUESTED_AT='' RESTART_AT='' MESSAGE=''

install_units() {
    # The API container runs as root, so requests/ is root-only on the host as well; status/ is mounted read-only.
    install -d -m 0755 "$restart_dir"
    install -d -m 0750 "$status_dir"
    install -d -m 0700 "$requests"
    cat > "$systemd_dir/automaton-restart.service" <<UNIT
[Unit]
Description=Restart the server as requested from Studio, the CLI or an approved app
StartLimitIntervalSec=0

[Service]
Type=oneshot
ExecStart=/usr/bin/env bash $install_dir/restart-from-request.sh --install-dir $install_dir
UNIT
    cat > "$systemd_dir/automaton-restart.path" <<UNIT
[Unit]
Description=Watch for Automaton server restart requests

[Path]
PathExists=$requests/request.json
Unit=automaton-restart.service
TriggerLimitIntervalSec=0

[Install]
WantedBy=multi-user.target
UNIT
    chmod 0644 "$systemd_dir/automaton-restart.service" "$systemd_dir/automaton-restart.path"
    if [[ "$systemd_dir" == /etc/systemd/system ]]; then
        systemctl daemon-reload \
            && systemctl enable --now automaton-restart.path >/dev/null \
            && systemctl is-active --quiet automaton-restart.path \
            || { echo 'automaton-restart.path could not be enabled. Check: systemctl status automaton-restart.path' >&2; return 1; }
    fi
    echo 'Restart requests from Studio, the CLI and approved apps enabled (automaton-restart.path).'
}

# Writes status.json atomically. Values come from the shell variables above, never from the request.
write_status() {
    STATE="$1" python3 - "$status_dir/status.json" <<'PY'
import json, os, sys
env = os.environ
def value(name):
    return env.get(name) or None
status = {
    'state': env['STATE'],
    'requestedBy': value('REQUESTED_BY'),
    'requestedAtUtc': value('REQUESTED_AT'),
    'restartAtUtc': value('RESTART_AT'),
    'message': value('MESSAGE'),
}
target = sys.argv[1]
temporary = target + '.tmp'
with open(temporary, 'w') as stream:
    json.dump(status, stream, indent=2)
os.chmod(temporary, 0o640)
os.replace(temporary, target)
PY
}

read_status_field() {
    python3 - "$status_dir/status.json" "$1" <<'PY' 2>/dev/null
import json, sys
try:
    value = json.load(open(sys.argv[1])).get(sys.argv[2])
except (OSError, ValueError, AttributeError):
    value = None
print(value or '')
PY
}

# Prints why the server must not restart now, or nothing when it may.
busy_reason() {
    if [[ -e "$backup_lock" ]] && ! { exec 7<"$backup_lock" && flock -n 7; }; then
        echo 'A backup is running.'; return
    fi
    exec 7<&- 2>/dev/null || true
    if [[ -e "$update_dir/.lock" ]] && ! { exec 6<"$update_dir/.lock" && flock -n 6; }; then
        echo 'A server update is running.'; return
    fi
    exec 6<&- 2>/dev/null || true
    # Rebooting while apt or dpkg runs can leave packages half-installed.
    if [[ -e "$os_update_dir/.lock" ]] && ! { exec 5<"$os_update_dir/.lock" && flock -n 5; }; then
        echo 'An OS update is running.'; return
    fi
    exec 5<&- 2>/dev/null || true
    case "$(python3 - "$update_dir/status/status.json" <<'PY' 2>/dev/null
import json, sys
try:
    print(json.load(open(sys.argv[1])).get('state', ''))
except (OSError, ValueError, AttributeError):
    print('')
PY
)" in
        starting|running) echo 'A server update is running.' ;;
    esac
}

now() { date -u +%Y-%m-%dT%H:%M:%SZ; }

# Reads the request without following links or blocking on special files. Prints requestedBy and the delay in seconds
# (0 = now) on success; otherwise a fixed reason on stderr (parse details are never echoed) and exit 1.
validate_request() {
    python3 - "$request" <<'PY'
import datetime, json, os, re, stat, sys
def fail(reason):
    raise SystemExit(reason)
try:
    descriptor = os.open(sys.argv[1], os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0))
except OSError:
    fail('The restart request is invalid.')
try:
    if not stat.S_ISREG(os.fstat(descriptor).st_mode):
        fail('The restart request is invalid.')
    raw = os.read(descriptor, 4097)
finally:
    os.close(descriptor)
if len(raw) > 4096:
    fail('The restart request is invalid.')
try:
    request = json.loads(raw.decode('utf-8'))
except (ValueError, UnicodeDecodeError):
    fail('The restart request is invalid.')
if not isinstance(request, dict):
    fail('The restart request is invalid.')
requested_by = request.get('requestedBy')
if not isinstance(requested_by, str) or not re.fullmatch(r'[A-Za-z0-9@._+ -]{1,200}', requested_by):
    requested_by = 'unknown'
at = request.get('restartAtUtc')
delay = 0
restart_at = ''
if at is not None:
    if not isinstance(at, str) or not re.fullmatch(r'[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:.]+(Z|\+00:00)', at):
        fail('The restart time is invalid.')
    try:
        when = datetime.datetime.fromisoformat(at.replace('Z', '+00:00'))
    except ValueError:
        fail('The restart time is invalid.')
    seconds = (when - datetime.datetime.now(datetime.timezone.utc)).total_seconds()
    if seconds > 7 * 24 * 3600:
        fail('The restart time is more than 7 days away.')
    if seconds > 30:
        delay = int(seconds)
        restart_at = when.strftime('%Y-%m-%dT%H:%M:%SZ')
print(requested_by)
print(delay)
print(restart_at)
PY
}

reboot_now() {
    write_status rebooting
    if [[ -n "${AUTOMATON_RESTART_TEST:-}" ]]; then echo reboot > "$restart_dir/reboot-called"; return 0; fi
    systemctl reboot
}

do_reboot() {
    install -d -m 0750 "$status_dir"
    REQUESTED_BY=$(read_status_field requestedBy); REQUESTED_AT=$(read_status_field requestedAtUtc)
    RESTART_AT=$(read_status_field restartAtUtc)
    local reason
    reason=$(busy_reason)
    if [[ -n "$reason" ]]; then
        MESSAGE="The restart was cancelled: $reason Request it again when that has finished."
        write_status refused
        # Succeed so the transient unit does not stay "failed" and block the next scheduled restart.
        return 0
    fi
    reboot_now
}

run_request() {
    install -d -m 0750 "$status_dir"
    install -d -m 0700 "$requests"
    exec 8>"$restart_dir/.lock"
    if ! flock -n 8; then
        rm -rf -- "$request"
        echo 'A restart is already being processed; the new request was refused.' >&2
        return 0
    fi
    [[ -e "$request" || -L "$request" ]] || return 0

    local reason delay parsed
    REQUESTED_AT=$(now) REQUESTED_BY='' RESTART_AT='' MESSAGE=''
    if ! reason=$(validate_request 2>&1 >"$restart_dir/parsed.txt"); then
        rm -rf -- "$request" "$restart_dir/parsed.txt"
        case "$reason" in "The restart request is invalid."|"The restart time"*) ;; *) reason='The restart request is invalid.' ;; esac
        MESSAGE="$reason"
        write_status refused
        return 1
    fi
    mapfile -t parsed < <(tr -d '\r' < "$restart_dir/parsed.txt")
    rm -f -- "$restart_dir/parsed.txt"
    REQUESTED_BY="${parsed[0]}"; delay="${parsed[1]}"; RESTART_AT="${parsed[2]:-}"
    rm -rf -- "$request"

    if [[ -z "${AUTOMATON_RESTART_TEST:-}" ]] && systemctl is-active --quiet automaton-restart-scheduled.timer; then
        MESSAGE='A restart is already scheduled; the new request was refused.'
        write_status refused
        return 1
    fi
    # A scheduled restart is checked again when its time comes; only an immediate one is checked now.
    reason=''
    ((delay > 0)) || reason=$(busy_reason)
    if [[ -n "$reason" ]]; then
        MESSAGE="The restart was refused: $reason Request it again when that has finished."
        write_status refused
        return 1
    fi
    if ((delay > 0)); then
        write_status scheduled
        if [[ -n "${AUTOMATON_RESTART_TEST:-}" ]]; then echo "delay=$delay" > "$restart_dir/scheduled"; return 0; fi
        systemctl reset-failed automaton-restart-scheduled.service automaton-restart-scheduled.timer >/dev/null 2>&1 || true
        systemd-run --quiet --unit=automaton-restart-scheduled --on-active="${delay}s" --timer-property=AccuracySec=1s \
            /usr/bin/env bash "$install_dir/restart-from-request.sh" --reboot --install-dir "$install_dir" \
            || { MESSAGE='The restart could not be scheduled.'; write_status refused; return 1; }
        return 0
    fi
    reboot_now
}

main() {
    case "$mode" in
        units) install_units ;;
        reboot) do_reboot ;;
        run)
            run_request
            local code=$?
            # If the request could not be consumed (for example a read-only disk), do not spin the path unit.
            if [[ -e "$request" || -L "$request" ]]; then sleep 10; fi
            return "$code" ;;
    esac
}
main
exit $?
