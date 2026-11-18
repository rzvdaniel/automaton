#!/usr/bin/env bash
# Keep scanners and password guessers out of an Automaton server: install fail2ban and its jails (sshd,
# automaton-scanner, automaton-signin) that read Traefik's JSON access log. Safe to run again; the installer
# and every update run it. Real traffic is never limited: only junk paths, 404 bursts and failed sign-ins ban.
#
#   fail2ban.sh [--install-dir DIR]
#
# The address this script is run from over SSH is added to the sshd jail's ignoreip (failed logins before the
# install would otherwise ban the administrator).
# Never ban an address: add it to "ignoreip =" in the [DEFAULT] section of /etc/fail2ban/jail.local and run
# sudo fail2ban-client reload; all three jails extend that value (%(known/ignoreip)s), they do not copy it.
set -euo pipefail
umask 022

install_dir=/opt/automaton-server
while (($#)); do
    case "$1" in
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --help) sed -n '2,12p' "$0"; exit 0 ;;
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
# The address this script runs from over SSH is never banned from sshd: failed logins before the install (a wrong key)
# are still in the journal and would lock the administrator out. Addresses added earlier stay (kept in a comment line).
admin_ips=$(sed -n 's/^# admin-ssh-ip://p' "$f2b_dir/jail.d/automaton.conf" 2>/dev/null | tr -s '[:space:]' ' ' || true)
ssh_client=${SSH_CONNECTION:-}; ssh_client=${ssh_client%% *}
[[ -n "$ssh_client" ]] || ssh_client=$(who -m 2>/dev/null | sed -n 's/.*(\(.*\)).*/\1/p' || true)
# Only a real IPv4/IPv6 address counts ("who -m" can print ":0" for a local session).
python3 -c 'import ipaddress,sys; ipaddress.ip_address(sys.argv[1])' "$ssh_client" 2>/dev/null || ssh_client=
admin_ips=$(printf '%s\n' $admin_ips $ssh_client | awk 'NF && !seen[$0]++' | paste -sd' ')
tmp_jail=$(mktemp "$f2b_dir/jail.d/.automaton.XXXXXX")
python3 - "$source_dir/jail.conf.in" "$traefik_log" "$admin_ips" > "$tmp_jail" <<'PY'
import sys
text = open(sys.argv[1]).read().replace('@TRAEFIK_LOG@', sys.argv[2]).replace('@ADMIN_IPS@', sys.argv[3])
print(text + (f'\n# admin-ssh-ip: {sys.argv[3]}\n' if sys.argv[3] else ''), end='')
PY
chmod 0644 "$tmp_jail"
mv "$tmp_jail" "$f2b_dir/jail.d/automaton.conf"
# An allowlist file from an earlier version would override the live allowlist in the jails.
rm -f "$f2b_dir/jail.d/automaton-allowlist.local"
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
    # Earlier versions ignored the chain setting and hooked the jails into INPUT, where bans do not reach Docker's ports.
    for ipt in iptables ip6tables; do
        command -v "$ipt" >/dev/null || continue
        while read -r rule; do "$ipt" $(sed 's/^-A/-D/' <<<"$rule") || true; done \
            < <("$ipt" -S INPUT 2>/dev/null | grep -E -- '^-A INPUT .*-j f2b-automaton-' || true)
    done
fi
echo "fail2ban protects this server: jails sshd, automaton-scanner and automaton-signin read $traefik_log."
