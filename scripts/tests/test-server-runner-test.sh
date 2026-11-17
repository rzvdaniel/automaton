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
if SUDO_USER= bash "$s" remove sh_0123456789abcdef0123456789abcdef >/dev/null 2>&1; then echo "FAIL: accepted without sudo"; fail=1; fi
[[ $fail -eq 0 ]] && echo "ok"; exit $fail
