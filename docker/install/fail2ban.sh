#!/usr/bin/env bash
# Keep scanners and password guessers out of an Automaton server: install fail2ban and its jails (sshd,
# automaton-scanner, automaton-signin) that read Traefik's JSON access log. Safe to run again; the installer
# and every update run it. Real traffic is never limited: only junk paths, 404 bursts and failed sign-ins ban.
#
#   fail2ban.sh [--install-dir DIR]
#
# Never ban an address: put it after "ignoreip =" in /etc/fail2ban/jail.d/automaton-allowlist.local, then
# run: sudo fail2ban-client reload
set -euo pipefail
umask 022

install_dir=/opt/automaton-server
while (($#)); do
    case "$1" in
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --help) sed -n '2,10p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done
[[ $EUID -eq 0 || -n "${AUTOMATON_FAIL2BAN_ETC:-}" ]] || { echo 'Run as root (sudo).' >&2; exit 1; }
f2b_dir=${AUTOMATON_FAIL2BAN_ETC:-/etc/fail2ban}
logrotate_dir=${AUTOMATON_LOGROTATE_DIR:-/etc/logrotate.d}
source_dir="$install_dir/fail2ban"
[[ -f "$source_dir/jail.conf.in" && -d "$source_dir/filter.d" ]] || { echo "The fail2ban files are missing from $install_dir." >&2; exit 1; }

if ! command -v fail2ban-client >/dev/null; then
    command -v apt-get >/dev/null || { echo 'Install fail2ban with your package manager, then run this script again.' >&2; exit 1; }
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install -y fail2ban
fi
command -v docker >/dev/null || { echo 'Docker is required.' >&2; exit 1; }
volume=$(docker volume ls -q --filter label=com.docker.compose.project=automaton-server --filter label=com.docker.compose.volume=certificates | head -n1)
[[ -n "$volume" ]] || { echo 'The server certificates volume was not found.' >&2; exit 1; }
traefik_log="$(docker volume inspect -f '{{.Mountpoint}}' "$volume")/logs/access.log"
# fail2ban refuses to start a jail whose log does not exist yet; Traefik appends to this file.
install -d -m 0755 "$(dirname "$traefik_log")"
[[ -e "$traefik_log" ]] || install -m 0640 /dev/null "$traefik_log"

install -d "$f2b_dir/filter.d" "$f2b_dir/jail.d" "$logrotate_dir"
for filter in "$source_dir"/filter.d/*.conf; do install -m 0644 "$filter" "$f2b_dir/filter.d/$(basename "$filter")"; done
sed "s#@TRAEFIK_LOG@#$traefik_log#" "$source_dir/jail.conf.in" > "$f2b_dir/jail.d/automaton.conf"
# The allowlist belongs to the owner: created once, never overwritten.
[[ -e "$f2b_dir/jail.d/automaton-allowlist.local" ]] || install -m 0644 "$source_dir/allowlist.conf.in" "$f2b_dir/jail.d/automaton-allowlist.local"
cat > "$logrotate_dir/automaton-traefik" <<LOGROTATE
$traefik_log {
    daily
    maxsize 100M
    rotate 7
    missingok
    notifempty
    compress
    delaycompress
    copytruncate
}
LOGROTATE
if [[ -z "${AUTOMATON_FAIL2BAN_ETC:-}" ]]; then
    fail2ban-client -t >/dev/null || { echo 'The fail2ban configuration did not validate; the Automaton jails were not activated.' >&2; rm -f "$f2b_dir/jail.d/automaton.conf"; exit 1; }
    # Bans live in Docker's DOCKER-USER chain, so fail2ban must start after Docker at boot.
    install -d /etc/systemd/system/fail2ban.service.d
    printf '[Unit]\nAfter=docker.service\n' > /etc/systemd/system/fail2ban.service.d/automaton.conf
    systemctl daemon-reload
    systemctl enable --now fail2ban
    fail2ban-client reload >/dev/null
fi
echo "fail2ban protects this server: jails sshd, automaton-scanner and automaton-signin read $traefik_log. Allowlist: $f2b_dir/jail.d/automaton-allowlist.local"
