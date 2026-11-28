#!/usr/bin/env bash
# Unit tests for wait_install_lock and restore_failure_message in scripts/ci/release-install-check.sh.
#   bash scripts/tests/release-install-check-test.sh
set -euo pipefail

repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
fail_test() { echo "FAIL: $*" >&2; exit 1; }

source <(sed -n '/^# restore-wait:begin/,/^# restore-wait:end/p' "$repo/scripts/ci/release-install-check.sh")

on_failure_calls=0
last_fail=''
on_failure() { on_failure_calls=$((on_failure_calls + 1)); }
fail() { last_fail="$*"; return 1; }

# 1. Real flock held briefly, then released: wait_install_lock polls and succeeds.
lock="$work/install.lock"
remote() { bash -c "${1//\/run\/lock\/automaton-install.lock/$lock}"; }
(
    exec 9>"$lock"
    flock -x 9
    sleep 0.4
) &
holder=$!
sleep 0.1
INSTALL_LOCK_WAIT_SECONDS=5 INSTALL_LOCK_POLL_SECONDS=0.1 wait_install_lock || fail_test "wait_install_lock failed after lock release: $last_fail"
wait "$holder"
[[ "$on_failure_calls" -eq 0 ]] || fail_test 'on_failure should not be called when lock releases'

# 2. Real flock held past deadline: wait_install_lock times out with lock-specific message.
(
    exec 9>"$lock"
    flock -x 9
    sleep 3
) &
holder=$!
sleep 0.1
on_failure_calls=0
last_fail=''
if INSTALL_LOCK_WAIT_SECONDS=1 INSTALL_LOCK_POLL_SECONDS=0.2 wait_install_lock; then
    kill "$holder" 2>/dev/null || true
    fail_test 'expected wait_install_lock to time out while lock was held'
fi
kill "$holder" 2>/dev/null || true
wait "$holder" 2>/dev/null || true
[[ "$on_failure_calls" -eq 1 ]] || fail_test "expected 1 on_failure call, got $on_failure_calls"
[[ "$last_fail" == *"First-boot install did not finish and release /run/lock/automaton-install.lock within 1s"* ]] \
    || fail_test "unexpected lock timeout message: $last_fail"

# 3. SSH failure past deadline: timeout message reports SSH/command exit code, not lock held.
remote() { return 255; }
on_failure_calls=0
last_fail=''
if INSTALL_LOCK_WAIT_SECONDS=1 INSTALL_LOCK_POLL_SECONDS=0.2 wait_install_lock; then
    fail_test 'expected wait_install_lock to fail on SSH error'
fi
[[ "$last_fail" == *"Could not check /run/lock/automaton-install.lock via SSH within 1s (last exit 255)"* ]] \
    || fail_test "unexpected SSH timeout message: $last_fail"

# 4. restore_failure_message names the real cause.
printf 'Another installation, update or restore is running.\n' > "$work/lock.log"
msg=$(restore_failure_message "$work/lock.log")
[[ "$msg" == *'holding /run/lock/automaton-install.lock'* && "$msg" != *key* ]] \
    || fail_test "unexpected lock failure message: $msg"

printf 'age: error: no identity matched any of the recipients\n' > "$work/key.log"
msg=$(restore_failure_message "$work/key.log")
[[ "$msg" == *'decryption failed'* && "$msg" == *key* ]] \
    || fail_test "unexpected decryption failure message: $msg"

printf 'Archive or key file not found.\n' > "$work/missing.log"
msg=$(restore_failure_message "$work/missing.log")
[[ "$msg" == *'not found'* && "$msg" != *decryption* ]] \
    || fail_test "unexpected missing-file message: $msg"

printf 'pg_restore: error: could not execute query\n' > "$work/other.log"
msg=$(restore_failure_message "$work/other.log")
[[ "$msg" == *'did not finish'* && "$msg" != *key* && "$msg" != *decryption* ]] \
    || fail_test "unexpected generic failure message: $msg"

echo 'release-install-check-test: ok'
