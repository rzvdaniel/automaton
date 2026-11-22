#!/usr/bin/env bash
# Retires the host systemd cleanup timer (automaton-cleanup.timer, issue #1083). The nightly cleanup now runs in the
# Runner on a schedule the server stores, and administrators change it or run it now through the API and Server Console.
#
#   cleanup.sh --remove-timer   stop, disable and delete automaton-cleanup.timer and its service (safe to repeat)
set -euo pipefail

while (($#)); do
    case "$1" in
        --install-dir) shift 2 ;;
        --remove-timer) shift ;;
        --help) sed -n '2,6p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done
# AUTOMATON_UPDATE_TEST lets the script tests run without root against a temporary directory.
systemd_dir=${AUTOMATON_SYSTEMD_DIR:-/etc/systemd/system}
[[ -n "${AUTOMATON_UPDATE_TEST:-}" || $EUID -eq 0 ]] || { echo 'Run as root (sudo).' >&2; exit 1; }

if [[ -f "$systemd_dir/automaton-cleanup.timer" || -f "$systemd_dir/automaton-cleanup.service" ]]; then
    if [[ -z "${AUTOMATON_UPDATE_TEST:-}" ]] && command -v systemctl >/dev/null; then
        systemctl disable --now automaton-cleanup.timer >/dev/null 2>&1 || true
    fi
    rm -f "$systemd_dir/automaton-cleanup.timer" "$systemd_dir/automaton-cleanup.service"
    if [[ -z "${AUTOMATON_UPDATE_TEST:-}" ]] && command -v systemctl >/dev/null; then systemctl daemon-reload || true; fi
    echo 'Removed the old host cleanup timer; the Runner now runs the nightly cleanup.'
fi
