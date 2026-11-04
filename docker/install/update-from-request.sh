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
# Only a version ("latest" or server-vX.Y.Z) is taken from the request and passed to update.sh; nothing in the
# file is ever executed or interpolated into a command.
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
if [[ -z "${AUTOMATON_UPDATE_TEST:-}" && $EUID != 0 ]]; then echo 'Run with sudo.' >&2; exit 1; fi

requests="$update_dir/requests"
status_dir="$update_dir/status"
work="$update_dir/work"
version_pattern='^server-v[0-9]+\.[0-9]+\.[0-9]+$'
# Read by write_status.
export REQUESTED_VERSION='' FROM_VERSION='' STARTED='' REQUESTED_BY='' RECOVERY_BACKUP='' MESSAGE=''

install_units() {
    # The API container runs as root, so requests/ is root-only on the host as well; status/ is mounted read-only.
    install -d -m 0755 "$update_dir" "$status_dir"
    install -d -m 0700 "$requests" "$work"
    cat > "$systemd_dir/automaton-update.service" <<UNIT
[Unit]
Description=Run an Automaton server update requested from Studio or the CLI
After=docker.service
Requires=docker.service

[Service]
Type=oneshot
ExecStart=/usr/bin/env bash $install_dir/update-from-request.sh --install-dir $install_dir
UNIT
    cat > "$systemd_dir/automaton-update.path" <<UNIT
[Unit]
Description=Watch for Automaton server update requests

[Path]
PathExists=$requests/request.json
Unit=automaton-update.service

[Install]
WantedBy=multi-user.target
UNIT
    chmod 0644 "$systemd_dir/automaton-update.service" "$systemd_dir/automaton-update.path"
    if [[ "$systemd_dir" == /etc/systemd/system ]]; then
        systemctl daemon-reload
        systemctl enable --now automaton-update.path >/dev/null
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
os.chmod(temporary, 0o644)
os.replace(temporary, target)
PY
}

# Keeps the last 200 log lines without secret-looking values. status/ is readable by the API container.
publish_log() {
    python3 - "$work/run.log" "$status_dir/update.log" <<'PY'
import os, re, sys
try:
    lines = open(sys.argv[1], errors='replace').read().splitlines()[-200:]
except FileNotFoundError:
    lines = []
secret = re.compile(r'(?i)((?:password|passwd|secret|token|api[_-]?key|authorization)\S*\s*[=:]\s*)\S+')
text = '\n'.join(secret.sub(r'\1***', line) for line in lines) + '\n'
temporary = sys.argv[2] + '.tmp'
with open(temporary, 'w') as stream:
    stream.write(text)
os.chmod(temporary, 0o644)
os.replace(temporary, sys.argv[2])
PY
}

now() { date -u +%Y-%m-%dT%H:%M:%SZ; }

# Prints "<version>" for a valid request, or a reason on stderr and a nonzero exit.
validate_request() {
    python3 - "$1" <<'PY'
import json, re, sys
path = sys.argv[1]
try:
    with open(path, 'rb') as stream:
        raw = stream.read(4097)
    if len(raw) > 4096:
        raise ValueError('The request is too large.')
    request = json.loads(raw.decode('utf-8'))
except (ValueError, UnicodeDecodeError) as error:
    raise SystemExit(f'The update request is not valid JSON. {error}')
if not isinstance(request, dict):
    raise SystemExit('The update request must be a JSON object.')
version = request.get('version', 'latest')
if not isinstance(version, str) or not (version == 'latest' or re.fullmatch(r'server-v[0-9]+\.[0-9]+\.[0-9]+', version)):
    raise SystemExit('The requested version must be "latest" or server-vX.Y.Z.')
requested_by = request.get('requestedBy')
if not isinstance(requested_by, str) or not re.fullmatch(r'[A-Za-z0-9@._+ -]{1,200}', requested_by):
    requested_by = 'unknown'
print(version)
print(requested_by)
PY
}

run_request() {
    [[ -d "$requests" && -d "$status_dir" ]] || { echo 'Update directories are missing. Run update-from-request.sh --install-units.' >&2; return 1; }
    install -d -m 0700 "$work"
    exec 8>"$update_dir/.lock"
    if ! flock -n 8; then
        # automaton-update.service never runs twice at once; this only guards a manual run.
        rm -f -- "$requests/request.json"
        echo 'An update is already running; the new request was refused.' >&2
        return 0
    fi
    local request="$requests/request.json"
    [[ -e "$request" || -L "$request" ]] || return 0
    rm -f -- "$work/request.json"
    if [[ -f "$request" && ! -L "$request" ]]; then
        mv -T -- "$request" "$work/request.json"
    else
        rm -f -- "$request"
        REQUESTED_VERSION='' FROM_VERSION='' STARTED=$(now) REQUESTED_BY='' RECOVERY_BACKUP='' MESSAGE='The update request was not a regular file.'
        write_status failed 1 '' "$(now)"
        return 1
    fi

    local parsed reason
    REQUESTED_VERSION='' FROM_VERSION='' STARTED=$(now) REQUESTED_BY='' RECOVERY_BACKUP='' MESSAGE=''
    if ! reason=$(validate_request "$work/request.json" 2>&1 >"$work/parsed.txt"); then
        rm -f -- "$work/request.json" "$work/parsed.txt"
        MESSAGE="$reason"
        write_status failed 1 '' "$(now)"
        : > "$work/run.log"; printf '%s\n' "$reason" > "$work/run.log"; publish_log
        return 1
    fi
    mapfile -t parsed < <(tr -d '\r' < "$work/parsed.txt")
    rm -f -- "$work/request.json" "$work/parsed.txt"
    REQUESTED_VERSION="${parsed[0]}"
    REQUESTED_BY="${parsed[1]}"
    FROM_VERSION=$(tr -d '\r\n' < "$install_dir/version" 2>/dev/null || true)
    [[ "$FROM_VERSION" =~ $version_pattern ]] || FROM_VERSION=''

    : > "$work/run.log"
    publish_log
    write_status running
    local arguments=(--install-dir "$install_dir")
    [[ "$REQUESTED_VERSION" == latest ]] || arguments+=(--version "$REQUESTED_VERSION")
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
        run) run_request ;;
    esac
}
# Exit explicitly: the updater replaces this file while it runs.
main
exit $?
