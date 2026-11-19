#!/usr/bin/env bash
# Install the host's OS updates (apt) that an administrator requested from Studio, the CLI or an approved app.
#
# The API container never gets root. It only writes /var/lib/automaton/os-update/requests/request.json;
# automaton-os-update.path notices the file and automaton-os-update.service (root) runs this script, which validates
# the request, refuses while a backup, a server update or a restart is running, runs apt non-interactively (existing
# configuration files are kept) and writes status/status.json with the result, whether a restart is needed and a log tail.
#
#   os-update-from-request.sh --install-units   create the directories and systemd units (installer/updater)
#   os-update-from-request.sh                   process one pending request (started by automaton-os-update.service)
#
# Only requestedBy and scope (security or all) are taken from the request; nothing in the file is ever executed or
# interpolated into a command. The request is always removed before apt starts, so the path unit never loops.
set -uo pipefail
umask 077

install_dir=/opt/automaton-server
os_dir=${AUTOMATON_OS_UPDATE_DIR:-/var/lib/automaton/os-update}
update_dir=${AUTOMATON_UPDATE_DIR:-/var/lib/automaton/update}
restart_dir=${AUTOMATON_RESTART_DIR:-/var/lib/automaton/restart}
systemd_dir=${AUTOMATON_SYSTEMD_DIR:-/etc/systemd/system}
mode=run
while (($#)); do
    case "$1" in
        --install-units) mode=units; shift ;;
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --help) echo 'Usage: os-update-from-request.sh [--install-units] [--install-dir PATH]'; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done
[[ "$install_dir" =~ ^/[a-zA-Z0-9_./-]+$ && "$install_dir" != / ]] || { echo 'Use an absolute installation directory.' >&2; exit 1; }
# AUTOMATON_OS_UPDATE_TEST lets the script tests run without root and with a fake apt-get (AUTOMATON_OS_UPDATE_APT).
# systemd starts this script with a clean environment, so the API (which only writes request.json) can never set them.
test_mode=${AUTOMATON_OS_UPDATE_TEST:-}
if [[ -z "$test_mode" && $EUID != 0 ]]; then echo 'Run with sudo.' >&2; exit 1; fi
apt=apt-get
[[ -n "$test_mode" && -n "${AUTOMATON_OS_UPDATE_APT:-}" ]] && apt=$AUTOMATON_OS_UPDATE_APT
reboot_required=${AUTOMATON_REBOOT_REQUIRED_FILE:-/var/run/reboot-required}
updates_available=${AUTOMATON_UPDATES_AVAILABLE_FILE:-/var/lib/update-notifier/updates-available}

requests="$os_dir/requests"
status_dir="$os_dir/status"
request="$requests/request.json"
log_file="$status_dir/last.log"
backup_lock=${AUTOMATON_BACKUP_LOCK_FILE:-/run/lock/automaton-backup.lock}
export REQUESTED_BY='' REQUESTED_AT='' STARTED_AT='' FINISHED_AT='' SCOPE='' MESSAGE='' UPGRADED=''

install_units() {
    # The API container runs as root, so requests/ is root-only on the host as well; status/ is mounted read-only.
    install -d -m 0755 "$os_dir"
    install -d -m 0750 "$status_dir"
    install -d -m 0700 "$requests"
    cat > "$systemd_dir/automaton-os-update.service" <<UNIT
[Unit]
Description=Install OS updates as requested from Studio, the CLI or an approved app
StartLimitIntervalSec=0

[Service]
Type=oneshot
TimeoutStartSec=2h
ExecStart=/usr/bin/env bash $install_dir/os-update-from-request.sh --install-dir $install_dir
UNIT
    cat > "$systemd_dir/automaton-os-update.path" <<UNIT
[Unit]
Description=Watch for Automaton OS update requests

[Path]
PathExists=$requests/request.json
Unit=automaton-os-update.service
TriggerLimitIntervalSec=0

[Install]
WantedBy=multi-user.target
UNIT
    chmod 0644 "$systemd_dir/automaton-os-update.service" "$systemd_dir/automaton-os-update.path"
    if [[ "$systemd_dir" == /etc/systemd/system ]]; then
        systemctl daemon-reload \
            && systemctl enable --now automaton-os-update.path >/dev/null \
            && systemctl is-active --quiet automaton-os-update.path \
            || { echo 'automaton-os-update.path could not be enabled. Check: systemctl status automaton-os-update.path' >&2; return 1; }
    fi
    echo 'OS updates from Studio, the CLI and approved apps enabled (automaton-os-update.path).'
}

# Writes status.json atomically. Values come from the shell variables above, never from the request.
write_status() {
    STATE="$1" LOG_FILE="$log_file" REBOOT_REQUIRED_FILE="$reboot_required" python3 - "$status_dir/status.json" <<'PY'
import json, os, sys
env = os.environ
def value(name):
    return env.get(name) or None
tail = None
try:
    with open(env['LOG_FILE'], 'rb') as stream:
        stream.seek(0, 2)
        stream.seek(max(0, stream.tell() - 4000))
        tail = stream.read().decode('utf-8', 'replace')
except OSError:
    pass
upgraded = env.get('UPGRADED', '')
status = {
    'state': env['STATE'],
    'requestedBy': value('REQUESTED_BY'),
    'requestedAtUtc': value('REQUESTED_AT'),
    'startedAtUtc': value('STARTED_AT'),
    'finishedAtUtc': value('FINISHED_AT'),
    'scope': value('SCOPE'),
    'upgradedCount': int(upgraded) if upgraded.isdigit() else None,
    'restartRequired': os.path.exists(env['REBOOT_REQUIRED_FILE']) if env['STATE'] in ('succeeded', 'failed') else None,
    'message': value('MESSAGE'),
    'logTail': tail if env['STATE'] in ('succeeded', 'failed') else None,
}
target = sys.argv[1]
temporary = target + '.tmp'
with open(temporary, 'w') as stream:
    json.dump(status, stream, indent=2)
os.chmod(temporary, 0o640)
os.replace(temporary, target)
PY
}

# Prints why packages must not change now, or nothing when they may.
busy_reason() {
    if [[ -e "$backup_lock" ]] && ! { exec 7<"$backup_lock" && flock -n 7; }; then
        echo 'A backup is running.'; return
    fi
    exec 7<&- 2>/dev/null || true
    if [[ -e "$update_dir/.lock" ]] && ! { exec 6<"$update_dir/.lock" && flock -n 6; }; then
        echo 'A server update is running.'; return
    fi
    exec 6<&- 2>/dev/null || true
    case "$(python3 - "$update_dir/status/status.json" "$restart_dir/status/status.json" <<'PY' 2>/dev/null
import json, sys
def state(path):
    try:
        return json.load(open(path)).get('state', '')
    except (OSError, ValueError, AttributeError):
        return ''
if state(sys.argv[1]) in ('starting', 'running'):
    print('update')
elif state(sys.argv[2]) == 'rebooting':
    print('restart')
PY
)" in
        update) echo 'A server update is running.' ;;
        restart) echo 'The server is restarting.' ;;
    esac
}

now() { date -u +%Y-%m-%dT%H:%M:%SZ; }

# Reads the request without following links or blocking on special files. Prints requestedBy and the scope on success;
# otherwise a fixed reason on stderr (parse details are never echoed) and exit 1.
validate_request() {
    python3 - "$request" <<'PY'
import json, os, re, stat, sys
def fail(reason):
    raise SystemExit(reason)
try:
    descriptor = os.open(sys.argv[1], os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0))
except OSError:
    fail('The update request is invalid.')
try:
    if not stat.S_ISREG(os.fstat(descriptor).st_mode):
        fail('The update request is invalid.')
    raw = os.read(descriptor, 4097)
finally:
    os.close(descriptor)
if len(raw) > 4096:
    fail('The update request is invalid.')
try:
    request = json.loads(raw.decode('utf-8'))
except (ValueError, UnicodeDecodeError):
    fail('The update request is invalid.')
if not isinstance(request, dict):
    fail('The update request is invalid.')
requested_by = request.get('requestedBy')
if not isinstance(requested_by, str) or not re.fullmatch(r'[A-Za-z0-9@._+ -]{1,200}', requested_by):
    requested_by = 'unknown'
scope = request.get('scope', 'security')
if scope not in ('security', 'all'):
    fail('The update scope is invalid.')
print(requested_by)
print(scope)
PY
}

apt_env() { DEBIAN_FRONTEND=noninteractive NEEDRESTART_MODE=a "$@"; }

# Names of the packages with a pending security update (apt simulates the upgrade; no package changes).
security_packages() {
    apt_env "$apt" -s upgrade 2>/dev/null | awk '/^Inst / && /-security/ {print $2}'
}

# Runs apt and keeps the existing configuration files. Output goes to the log file, never to the status values.
run_apt() {
    # DPkg::Lock::Timeout waits for another apt or dpkg (for example unattended-upgrades after boot) instead of failing.
    local -a conf=(-y -o Dpkg::Options::=--force-confold -o DPkg::Lock::Timeout=300)
    if [[ "$SCOPE" == all ]]; then
        apt_env "$apt" "${conf[@]}" upgrade >>"$log_file" 2>&1
        return
    fi
    local -a packages
    mapfile -t packages < <(security_packages)
    if ((${#packages[@]} == 0)); then echo 'No security updates are waiting.' >>"$log_file"; return 0; fi
    apt_env "$apt" "${conf[@]}" --only-upgrade install "${packages[@]}" >>"$log_file" 2>&1
}

# apt's update-notifier hook rewrites the update summary as root-only (0600). The Runner reads it to show the update counts, so
# make it readable again after every apt run (a symlink or missing file is left alone).
restore_updates_summary_mode() {
    [[ -f "$updates_available" && ! -L "$updates_available" ]] && chmod 0644 -- "$updates_available" 2>/dev/null
    return 0
}

run_request() {
    install -d -m 0750 "$status_dir"
    install -d -m 0700 "$requests"
    exec 8>"$os_dir/.lock"
    if ! flock -n 8; then
        rm -rf -- "$request"
        echo 'An OS update is already running; the new request was refused.' >&2
        return 0
    fi
    [[ -e "$request" || -L "$request" ]] || return 0

    local reason parsed
    REQUESTED_AT=$(now) REQUESTED_BY='' SCOPE='' MESSAGE='' STARTED_AT='' FINISHED_AT='' UPGRADED=''
    if ! reason=$(validate_request 2>&1 >"$os_dir/parsed.txt"); then
        rm -rf -- "$request" "$os_dir/parsed.txt"
        case "$reason" in "The update request is invalid."|"The update scope"*) ;; *) reason='The update request is invalid.' ;; esac
        MESSAGE="$reason"
        write_status refused
        return 1
    fi
    mapfile -t parsed < <(tr -d '\r' < "$os_dir/parsed.txt")
    rm -f -- "$os_dir/parsed.txt"
    REQUESTED_BY="${parsed[0]}"; SCOPE="${parsed[1]}"
    rm -rf -- "$request"

    if [[ -z "$test_mode" ]] && ! command -v apt-get >/dev/null; then
        MESSAGE='This server does not use apt, so OS updates cannot be installed from Studio.'
        write_status unsupported
        return 1
    fi
    reason=$(busy_reason)
    if [[ -n "$reason" ]]; then
        MESSAGE="The update was refused: $reason Request it again when that has finished."
        write_status refused
        return 1
    fi

    STARTED_AT=$(now)
    : > "$log_file"
    write_status running
    local before after
    before=$(apt_env "$apt" -s upgrade 2>/dev/null | grep -c '^Inst ')
    local apt_ok=0
    run_apt && apt_ok=1
    restore_updates_summary_mode
    if ((apt_ok)); then
        after=$(apt_env "$apt" -s upgrade 2>/dev/null | grep -c '^Inst ')
        UPGRADED=$((before > after ? before - after : 0))
        FINISHED_AT=$(now)
        MESSAGE="Installed $UPGRADED update(s)."
        write_status succeeded
        return 0
    fi
    FINISHED_AT=$(now)
    MESSAGE='apt did not finish. See the log. Run "sudo dpkg --configure -a" on the server if it was interrupted.'
    write_status failed
    return 0
}

main() {
    case "$mode" in
        units) install_units ;;
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
