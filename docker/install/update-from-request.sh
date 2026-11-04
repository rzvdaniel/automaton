#!/usr/bin/env bash
# Run a server update that an administrator requested from Studio or the CLI.
#
# The API container never gets root. It only writes /var/lib/automaton/update/requests/request.json;
# automaton-update.path notices the file and automaton-update.service (root) runs this script, which
# validates the request, runs the installed update.sh and reports in status/status.json and status/update.log.
#
#   update-from-request.sh --install-units   create the update directories and systemd units (installer/updater)
#   update-from-request.sh                   process one pending request (started by automaton-update.service)
#
# Only a version ("latest" or server-vX.Y.Z) and an allowDowngrade flag are taken from the request and passed to
# update.sh; nothing in the file is ever executed or interpolated into a command. The request is always removed
# or moved aside before this script returns, so the path unit can never loop on it.
set -uo pipefail
umask 077

install_dir=/opt/automaton-server
update_dir=${AUTOMATON_UPDATE_DIR:-/var/lib/automaton/update}
systemd_dir=${AUTOMATON_SYSTEMD_DIR:-/etc/systemd/system}
mode=run
while (($#)); do
    case "$1" in
        --install-units) mode=units; shift ;;
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --help) echo 'Usage: update-from-request.sh [--install-units] [--install-dir PATH]'; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done
[[ "$install_dir" =~ ^/[a-zA-Z0-9_./-]+$ && "$install_dir" != / ]] || { echo 'Use an absolute installation directory.' >&2; exit 1; }
# AUTOMATON_UPDATE_TEST lets the script tests run without root against a temporary directory. systemd starts this
# script with a clean environment, so the API (which only writes request.json) can never set it.
if [[ -z "${AUTOMATON_UPDATE_TEST:-}" && $EUID != 0 ]]; then echo 'Run with sudo.' >&2; exit 1; fi

requests="$update_dir/requests"
status_dir="$update_dir/status"
work="$update_dir/work"
request="$requests/request.json"
version_pattern='^server-v[0-9]+\.[0-9]+\.[0-9]+$'
# Read by write_status.
export REQUESTED_VERSION='' FROM_VERSION='' STARTED='' REQUESTED_BY='' RECOVERY_BACKUP='' MESSAGE=''

install_units() {
    # The API container runs as root, so requests/ is root-only on the host as well; status/ is mounted read-only.
    install -d -m 0755 "$update_dir"
    install -d -m 0750 "$status_dir"
    install -d -m 0700 "$requests" "$work"
    cat > "$systemd_dir/automaton-update.service" <<UNIT
[Unit]
Description=Run an Automaton server update requested from Studio or the CLI
After=docker.service
Wants=docker.service
# Every run consumes its request; never let rapid requests permanently fail the unit.
StartLimitIntervalSec=0

[Service]
Type=oneshot
PrivateTmp=yes
ProtectHome=read-only
ExecStart=/usr/bin/env bash $install_dir/update-from-request.sh --install-dir $install_dir
UNIT
    cat > "$systemd_dir/automaton-update.path" <<UNIT
[Unit]
Description=Watch for Automaton server update requests

[Path]
PathExists=$requests/request.json
Unit=automaton-update.service
TriggerLimitIntervalSec=0

[Install]
WantedBy=multi-user.target
UNIT
    chmod 0644 "$systemd_dir/automaton-update.service" "$systemd_dir/automaton-update.path"
    if [[ "$systemd_dir" == /etc/systemd/system ]]; then
        systemctl daemon-reload \
            && systemctl enable --now automaton-update.path >/dev/null \
            && systemctl is-active --quiet automaton-update.path \
            || { echo 'automaton-update.path could not be enabled. Check: systemctl status automaton-update.path' >&2; return 1; }
    fi
    echo 'Update requests from Studio and the CLI enabled (automaton-update.path).'
}

# Writes status.json atomically. Values come from the shell variables below, never from the request.
write_status() {
    STATE="$1" EXIT_CODE="${2:-}" TO_VERSION="${3:-}" FINISHED="${4:-}" python3 - "$status_dir/status.json" <<'PY'
import json, os, sys
env = os.environ
def value(name):
    return env.get(name) or None
status = {
    'state': env['STATE'],
    'requestedVersion': value('REQUESTED_VERSION'),
    'fromVersion': value('FROM_VERSION'),
    'toVersion': value('TO_VERSION'),
    'startedAtUtc': value('STARTED'),
    'finishedAtUtc': value('FINISHED'),
    'exitCode': int(env['EXIT_CODE']) if env.get('EXIT_CODE') else None,
    'recoveryBackup': value('RECOVERY_BACKUP'),
    'requestedBy': value('REQUESTED_BY'),
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

# Keeps the last 200 log lines without secret-looking values. status/ is mounted into the API container.
publish_log() {
    python3 - "$work/run.log" "$status_dir/update.log" <<'PY'
import os, re, sys
try:
    with open(sys.argv[1], 'rb') as stream:
        stream.seek(0, os.SEEK_END)
        size = stream.tell()
        stream.seek(max(0, size - 256 * 1024))
        lines = stream.read().decode('utf-8', errors='replace').splitlines()[-200:]
except FileNotFoundError:
    lines = []
quoted = re.compile(r'(?i)("[^"\n]*(?:password|passwd|secret|token|api[_-]?key|authorization)[^"\n]*"\s*:\s*)"[^"\n]*"')
scheme = re.compile(r'(?i)\b(bearer|basic)\s+[A-Za-z0-9._~+/=-]+')
plain = re.compile(r'(?i)((?:password|passwd|secret|token|api[_-]?key|authorization)\S*\s*[=:]\s*)\S+')
def mask(line):
    line = quoted.sub(r'\1"***"', line)
    line = scheme.sub(r'\1 ***', line)
    return plain.sub(r'\1***', line)
temporary = sys.argv[2] + '.tmp'
with open(temporary, 'w') as stream:
    stream.write('\n'.join(mask(line) for line in lines) + '\n')
os.chmod(temporary, 0o640)
os.replace(temporary, sys.argv[2])
PY
}

now() { date -u +%Y-%m-%dT%H:%M:%SZ; }

# Reads the request without following links or blocking on special files. Prints version, requestedBy and
# allowDowngrade on success; otherwise a fixed reason on stderr (parse details are never echoed) and exit 1.
validate_request() {
    python3 - "$request" "$1" <<'PY'
import json, os, re, stat, sys
path, installed = sys.argv[1], sys.argv[2]
def fail(reason):
    raise SystemExit(reason)
try:
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0))
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
version = request.get('version', 'latest')
if not isinstance(version, str) or not (version == 'latest' or re.fullmatch(r'server-v[0-9]+\.[0-9]+\.[0-9]+', version)):
    fail('The requested version must be "latest" or server-vX.Y.Z.')
allow_downgrade = request.get('allowDowngrade', False)
if not isinstance(allow_downgrade, bool):
    fail('The update request is invalid.')
def parse(tag):
    return tuple(int(part) for part in tag.removeprefix('server-v').split('.'))
if version != 'latest' and not allow_downgrade and re.fullmatch(r'server-v[0-9]+\.[0-9]+\.[0-9]+', installed) \
        and parse(version) < parse(installed):
    fail('The requested version is older than the installed one. A downgrade must be requested explicitly.')
requested_by = request.get('requestedBy')
if not isinstance(requested_by, str) or not re.fullmatch(r'[A-Za-z0-9@._+ -]{1,200}', requested_by):
    requested_by = 'unknown'
print(version)
print(requested_by)
print('true' if allow_downgrade else 'false')
PY
}

run_request() {
    install -d -m 0755 "$update_dir"
    install -d -m 0750 "$status_dir"
    install -d -m 0700 "$requests" "$work"
    exec 8>"$update_dir/.lock"
    if ! flock -n 8; then
        # automaton-update.service never runs twice at once; this only guards a manual run.
        rm -rf -- "$request"
        echo 'An update is already running; the new request was refused.' >&2
        return 0
    fi
    [[ -e "$request" || -L "$request" ]] || return 0

    local parsed reason installed allow=false
    installed=$(tr -d '\r\n' < "$install_dir/version" 2>/dev/null || true)
    FROM_VERSION=''
    [[ "$installed" =~ $version_pattern ]] && FROM_VERSION="$installed"
    REQUESTED_VERSION='' STARTED=$(now) REQUESTED_BY='' RECOVERY_BACKUP='' MESSAGE=''
    if ! reason=$(validate_request "$installed" 2>&1 >"$work/parsed.txt"); then
        rm -rf -- "$request" "$work/parsed.txt"
        case "$reason" in "The update request is invalid."|"The requested version"*) ;; *) reason='The update request is invalid.' ;; esac
        MESSAGE="$reason"
        write_status failed 1 '' "$(now)"
        printf '%s\n' "$reason" > "$work/run.log"; publish_log
        return 1
    fi
    mapfile -t parsed < <(tr -d '\r' < "$work/parsed.txt")
    rm -f -- "$work/parsed.txt"
    REQUESTED_VERSION="${parsed[0]}"
    REQUESTED_BY="${parsed[1]}"
    [[ "${parsed[2]}" == true ]] && allow=true

    # Visible to the API before the request disappears, so an update is never "pending" and "idle" at once.
    : > "$work/run.log"
    publish_log
    write_status starting
    rm -rf -- "$work/request.json"
    mv -T -- "$request" "$work/request.json" 2>/dev/null || rm -rf -- "$request"
    rm -rf -- "$work/request.json"

    write_status running
    local arguments=(--install-dir "$install_dir")
    [[ "$REQUESTED_VERSION" == latest ]] || arguments+=(--version "$REQUESTED_VERSION")
    [[ "$allow" != true ]] || arguments+=(--allow-downgrade)
    bash "$install_dir/update.sh" "${arguments[@]}" > "$work/run.log" 2>&1 < /dev/null &
    local updater=$!
    while kill -0 "$updater" 2>/dev/null; do sleep 3; publish_log; done
    wait "$updater"
    local code=$?
    publish_log
    RECOVERY_BACKUP=$(sed -n 's/^Creating protected recovery backup at \(\/[^ ]*\)$/\1/p' "$work/run.log" | head -n 1)
    local to_version
    to_version=$(tr -d '\r\n' < "$install_dir/version" 2>/dev/null || true)
    [[ "$to_version" =~ $version_pattern ]] || to_version=''
    if ((code == 0)); then
        write_status succeeded 0 "$to_version" "$(now)"
    else
        MESSAGE="The update did not finish (exit code $code). The previous release and the recovery backup are kept; see update.log."
        write_status failed "$code" "$to_version" "$(now)"
    fi
    return "$code"
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
# Exit explicitly: the updater replaces this file while it runs.
main
exit $?
