#!/usr/bin/env bash
# Path checks of uninstall-automaton.sh. Runs in test mode against temp dirs (no root, no Docker needed).
set -u
s="$(dirname "$0")/../uninstall-automaton.sh"
t=$(mktemp -d); trap 'rm -rf "$t"' EXIT
fail=0
run() { AUTOMATON_UNINSTALL_TEST=1 PATH="$t/nobin:/usr/bin:/bin" bash "$s" --yes "$@" >"$t/out" 2>&1; }
expect_fail() { if run "$@"; then echo "FAIL: accepted: $*"; fail=1; fi; }
expect_ok() { run "$@" || { echo "FAIL: refused: $*"; cat "$t/out"; fail=1; }; }
export AUTOMATON_RUNNERS_DIR=$t/runners AUTOMATON_SYSTEMD_DIR=$t/systemd AUTOMATON_FAIL2BAN_ETC=$t/f2b AUTOMATON_STATE_DIR=$t/state AUTOMATON_LOGROTATE_DIR=$t/lr
mkdir -p "$t/other" "$t/inst/backups" "$t/cfg/secrets" "$t/state/update" "$t/state/apps"
touch "$t/other/precious" "$t/inst/compose.yaml" "$t/inst/x" "$t/inst/backups/b" "$t/cfg/installation.json" "$t/cfg/secrets/backup-key" "$t/cfg/secrets/s" "$t/state/update/u" "$t/state/apps/data"
expect_fail --install-dir /opt/../etc --config-dir "$t/cfg"
expect_fail --install-dir "$t/inst/../other" --config-dir "$t/cfg"
expect_fail --install-dir "$t/inst" --config-dir "$t/cfg/.."
expect_fail --install-dir / --config-dir "$t/cfg"
expect_fail --install-dir "$t/other" --config-dir "$t/cfg"          # exists, not an Automaton install
expect_fail --install-dir "$t/inst" --config-dir "$t/other"
[[ -e $t/other/precious ]] || { echo "FAIL: unrelated directory was touched"; fail=1; }
AUTOMATON_STATE_DIR=/ expect_fail --install-dir "$t/inst" --config-dir "$t/cfg"
AUTOMATON_STATE_DIR=$t/../x expect_fail --install-dir "$t/inst" --config-dir "$t/cfg"
AUTOMATON_RUNNERS_DIR=/ expect_fail --install-dir "$t/inst" --config-dir "$t/cfg"
# A recognised install is cleaned; backups, key and the Runner's app data stay.
expect_ok --install-dir "$t/inst" --config-dir "$t/cfg"
[[ -e $t/inst/x ]] && { echo "FAIL: install file kept"; fail=1; }
[[ -e $t/inst/backups/b && -e $t/cfg/secrets/backup-key && -e $t/state/apps/data ]] || { echo "FAIL: data not kept"; fail=1; }
[[ -e $t/state/update/u || -e $t/cfg/secrets/s ]] && { echo "FAIL: state/secret kept"; fail=1; }
expect_ok --install-dir "$t/inst" --config-dir "$t/cfg"   # second run: nothing left to remove
grep -q 'except backups' "$t/out" && { echo "FAIL: '(except backups)' printed with nothing removed"; fail=1; }
expect_ok --delete-data --install-dir "$t/inst" --config-dir "$t/cfg"
grep -q 'no volumes were removed' "$t/out" || { echo "FAIL: no warning about volumes"; fail=1; }
[[ -e $t/state/apps ]] && { echo "FAIL: --delete-data kept state"; fail=1; }
[[ $fail -eq 0 ]] && echo ok; exit $fail
