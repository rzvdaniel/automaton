#!/usr/bin/env bash
# Path checks of uninstall-automaton.sh. Runs in test mode against temp dirs (no root, no Docker needed).
set -u
s="$(dirname "$0")/../uninstall-automaton.sh"
t=$(mktemp -d); export TMPDIR=${TMPDIR:-/tmp}; trap 'rm -rf "$t"' EXIT
fail=0
run() { AUTOMATON_UNINSTALL_TEST=1 PATH="$t/bin:/usr/bin:/bin" bash "$s" --yes "$@" >"$t/out" 2>&1; }
expect_fail() { if run "$@"; then echo "FAIL: accepted: $*"; fail=1; fi; }
expect_ok() { run "$@" || { echo "FAIL: refused: $*"; cat "$t/out"; fail=1; }; }
export AUTOMATON_RUNNERS_DIR=$t/runners AUTOMATON_SYSTEMD_DIR=$t/systemd AUTOMATON_FAIL2BAN_ETC=$t/f2b AUTOMATON_STATE_DIR=$t/state AUTOMATON_LOGROTATE_DIR=$t/lr
mkdir -p "$t/other" "$t/inst/backups" "$t/cfg/secrets" "$t/state/update" "$t/state/apps"
mkdir -p "$t/bin" "$t/empty"
printf '#!/bin/sh\necho "docker called" >> "%s/docker-called"\nexit 1\n' "$t" > "$t/bin/docker"; chmod +x "$t/bin/docker"
printf 'name: automaton-server\n' > "$t/inst/compose.yaml"; touch "$t/inst/update.sh"
mkdir -p "$t/generic" "$t/cfg2"; printf 'name: other\n' > "$t/generic/compose.yaml"; touch "$t/generic/update.sh" "$t/cfg2/x"
printf 'name: automaton-server\n' > "$t/nomark.yaml"
mkdir -p "$t/systemd"; touch "$t/systemd/automaton-restart.path" "$t/systemd/automaton-restart.service" "$t/systemd/automaton-os-update.path" "$t/systemd/automaton-os-update.service"
touch "$t/other/precious" "$t/inst/x" "$t/inst/backups/b" "$t/cfg/installation.json" "$t/cfg/secrets/backup-key" "$t/cfg/secrets/s" "$t/state/update/u" "$t/state/apps/data"
expect_fail --install-dir /opt/../etc --config-dir "$t/cfg"
expect_fail --install-dir "$t/inst/../other" --config-dir "$t/cfg"
expect_fail --install-dir "$t/inst" --config-dir "$t/cfg/.."
expect_fail --install-dir / --config-dir "$t/cfg"
expect_fail --install-dir "$t/other" --config-dir "$t/cfg"          # exists, not an Automaton install
expect_fail --install-dir "$t/inst" --config-dir "$t/other"
expect_fail --install-dir "$t/generic" --config-dir "$t/cfg"        # generic compose.yaml + update.sh
expect_fail --install-dir "$t/empty" --config-dir "$t/cfg"          # empty directory
expect_fail --install-dir "$t/inst" --config-dir "$t/empty"
expect_fail --install-dir "$t/inst" --config-dir "$t/cfg2"          # no Automaton marker
mkdir "$t/inst2"; printf 'name: automaton-server\n' > "$t/inst2/compose.yaml"
expect_fail --install-dir "$t/inst2" --config-dir "$t/cfg"          # compose name but no update.sh/release.env
[[ -e $t/other/precious ]] || { echo "FAIL: unrelated directory was touched"; fail=1; }
AUTOMATON_STATE_DIR=/ expect_fail --install-dir "$t/inst" --config-dir "$t/cfg"
AUTOMATON_STATE_DIR=$t/../x expect_fail --install-dir "$t/inst" --config-dir "$t/cfg"
AUTOMATON_RUNNERS_DIR=/ expect_fail --install-dir "$t/inst" --config-dir "$t/cfg"
# Test mode needs every override, under the temp dir; outside test mode they are ignored.
( unset AUTOMATON_STATE_DIR; expect_fail --install-dir "$t/inst" --config-dir "$t/cfg" )
AUTOMATON_STATE_DIR=/var/lib expect_fail --install-dir "$t/inst" --config-dir "$t/cfg"
expect_fail --install-dir /opt/automaton-server --config-dir "$t/cfg"
[[ -e $t/docker-called ]] && { echo "FAIL: docker called in test mode"; fail=1; }
# Deliberately NOT run through run(): without AUTOMATON_UNINSTALL_TEST the script must ignore the overrides and use its fixed paths (dry-run, so nothing changes).
if PATH="$t/bin:/usr/bin:/bin" bash "$s" --dry-run --install-dir "$t/inst" --config-dir "$t/cfg" >"$t/out" 2>&1; then :; fi
grep -q 'Ignoring AUTOMATON_STATE_DIR' "$t/out" || { echo "FAIL: override not ignored outside test mode"; fail=1; }
grep -q 'Refusing\|Dry run' "$t/out" || { echo "FAIL: unexpected non-test output"; fail=1; }
[[ -e $t/state/update/u ]] || { echo "FAIL: non-test run used the override"; fail=1; }
# A recognised install is cleaned; backups, key and the Runner's app data stay.
expect_ok --install-dir "$t/inst" --config-dir "$t/cfg"
[[ -e $t/inst/x ]] && { echo "FAIL: install file kept"; fail=1; }
[[ -e $t/inst/backups/b && -e $t/cfg/secrets/backup-key && -e $t/state/apps/data ]] || { echo "FAIL: data not kept"; fail=1; }
[[ -e $t/state/update/u || -e $t/cfg/secrets/s ]] && { echo "FAIL: state/secret kept"; fail=1; }
[[ -e $t/systemd/automaton-restart.path || -e $t/systemd/automaton-restart.service || -e $t/systemd/automaton-os-update.path || -e $t/systemd/automaton-os-update.service ]] && { echo "FAIL: restart units kept"; fail=1; }
expect_ok --install-dir "$t/inst" --config-dir "$t/cfg"   # second run: nothing left to remove
grep -q 'except backups' "$t/out" && { echo "FAIL: '(except backups)' printed with nothing removed"; fail=1; }
grep -q 'volumes could not be checked' "$t/out" || { echo "FAIL: keep mode silent about unchecked volumes"; fail=1; }
[[ -f $t/inst/.automaton-uninstalled && -f $t/cfg/.automaton-uninstalled ]] || { echo "FAIL: keep mode wrote no marker"; fail=1; }
# A kept-only directory WITHOUT the marker is never accepted (it could be anything with a backups folder).
mkdir -p "$t/lookalike/backups" "$t/cfglook/secrets"; touch "$t/lookalike/backups/b" "$t/cfglook/secrets/backup-key"
expect_fail --install-dir "$t/lookalike" --config-dir "$t/cfg"
expect_fail --delete-data --install-dir "$t/inst" --config-dir "$t/cfglook"
[[ -e $t/lookalike/backups/b && -e $t/cfglook/secrets/backup-key ]] || { echo "FAIL: marker-less directory was deleted"; fail=1; }
# Parent symlinks are refused, not only a symlinked last component.
ln -s "$t/inst" "$t/link"; mkdir -p "$t/real/sub"; ln -s "$t/real" "$t/rlink"
expect_fail --install-dir "$t/link" --config-dir "$t/cfg"
expect_fail --install-dir "$t/rlink/sub" --config-dir "$t/cfg"
# Test mode refuses TMPDIR=/ and a symlinked or missing TMPDIR.
TMPDIR=/ expect_fail --install-dir "$t/inst" --config-dir "$t/cfg"
TMPDIR=$t/rlink expect_fail --install-dir "$t/inst" --config-dir "$t/cfg"
TMPDIR=$t/missing expect_fail --install-dir "$t/inst" --config-dir "$t/cfg"
expect_ok --delete-data --install-dir "$t/inst" --config-dir "$t/cfg"
grep -q 'no volumes were removed' "$t/out" || { echo "FAIL: no warning about volumes"; fail=1; }
[[ -e $t/state/apps ]] && { echo "FAIL: --delete-data kept state"; fail=1; }
[[ $fail -eq 0 ]] && echo ok; exit $fail
