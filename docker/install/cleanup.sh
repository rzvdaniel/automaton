#!/usr/bin/env bash
# Free disk space on an Automaton server. Runs nightly from automaton-cleanup.timer, after the backup.
# Removes Docker images no container uses and build cache older than 7 days, and trims the journal to 200 MB.
# Never volumes, images of existing containers or backups.
#
#   cleanup.sh                  clean now
#   cleanup.sh --install-timer  install and start the nightly systemd timer
set -euo pipefail

install_dir=/opt/automaton-server
mode=clean
while (($#)); do
    case "$1" in
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --install-timer) mode=timer; shift ;;
        --help) sed -n '2,8p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done
# AUTOMATON_UPDATE_TEST lets the script tests run without root against a temporary directory.
systemd_dir=${AUTOMATON_SYSTEMD_DIR:-/etc/systemd/system}
[[ -n "${AUTOMATON_UPDATE_TEST:-}" || $EUID -eq 0 ]] || { echo 'Run as root (sudo).' >&2; exit 1; }

install_timer() {
    cat > $systemd_dir/automaton-cleanup.service <<UNIT
[Unit]
Description=Automaton nightly Docker and journal cleanup
After=docker.service automaton-backup.service

[Service]
Type=oneshot
ExecStart=/usr/bin/env bash $install_dir/cleanup.sh --install-dir $install_dir
UNIT
    # The backup starts at 03:00 with up to an hour of random delay, so this runs well after it.
    cat > $systemd_dir/automaton-cleanup.timer <<'UNIT'
[Unit]
Description=Run the Automaton cleanup nightly, after the backup

[Timer]
OnCalendar=*-*-* 05:00:00
RandomizedDelaySec=30m
Persistent=true

[Install]
WantedBy=timers.target
UNIT
    chmod 0644 $systemd_dir/automaton-cleanup.service $systemd_dir/automaton-cleanup.timer
    systemctl daemon-reload
    systemctl enable --now automaton-cleanup.timer >/dev/null
    echo 'Nightly cleanup enabled (automaton-cleanup.timer).'
}

clean() {
    docker image prune -a -f --filter until=168h
    docker builder prune -f --filter until=168h
    journalctl --vacuum-size=200M
}

case "$mode" in
    timer) install_timer ;;
    clean) clean ;;
esac
