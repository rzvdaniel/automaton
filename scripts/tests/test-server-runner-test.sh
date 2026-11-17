#!/usr/bin/env bash
# Argument validation of test-server-runner.sh (no root needed: every case must fail before touching the disk).
set -u
s="$(dirname "$0")/../test-server-runner.sh"
fail=0
expect_fail() { if SUDO_USER=agent bash "$s" "$@" >/dev/null 2>&1; then echo "FAIL: accepted: $*"; fail=1; fi; }
expect_fail
expect_fail prepare
expect_fail prepare ../../etc
expect_fail remove sh_../../etc
expect_fail remove "sh_0123456789abcdef0123456789abcde"
expect_fail remove "sh_0123456789abcdef0123456789abcdeF"
expect_fail wipe sh_0123456789abcdef0123456789abcdef
# Valid arguments but no root-owned marker (the marker is never owned by the unprivileged test user): must refuse.
tmp=$(mktemp); trap 'rm -f "$tmp"' EXIT
for v in prepare remove; do
  if TEST_SERVER_MARKER=/nonexistent/.test-server SUDO_USER=agent bash "$s" $v sh_0123456789abcdef0123456789abcdef >/dev/null 2>&1; then echo "FAIL: $v ran without marker"; fail=1; fi
  if TEST_SERVER_MARKER="$tmp" SUDO_USER=agent bash "$s" $v sh_0123456789abcdef0123456789abcdef >/dev/null 2>&1; then echo "FAIL: $v ran with non-root marker"; fail=1; fi
done
if SUDO_USER=ubuntu TEST_SERVER_MARKER="$tmp" bash "$s" remove sh_0123456789abcdef0123456789abcdef >/dev/null 2>&1; then echo "FAIL: accepted other sudo user"; fail=1; fi
if SUDO_USER= bash "$s" remove sh_0123456789abcdef0123456789abcdef >/dev/null 2>&1; then echo "FAIL: accepted without sudo"; fail=1; fi
[[ $fail -eq 0 ]] && echo "ok"; exit $fail
