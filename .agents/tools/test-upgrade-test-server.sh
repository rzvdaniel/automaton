#!/usr/bin/env bash
# Offline test of upgrade-test-server.sh with fake update.sh and curl.
# Run: bash .agents/tools/test-upgrade-test-server.sh
set -euo pipefail
d=$(dirname "$(readlink -f "$0")")
t=$(mktemp -d)
trap 'rm -rf "$t"' EXIT

# Fake update.sh: writes the requested --version into UPGRADE_VERSION_FILE and logs the call.
cat > "$t/fake-update.sh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
echo "update $*" >> "$FAKE_LOG"
while [[ $# -gt 0 ]]; do
  if [[ "$1" == "--version" ]]; then
    echo "$2" > "$UPGRADE_VERSION_FILE"
    shift 2
  else
    shift
  fi
done
EOF
chmod +x "$t/fake-update.sh"

# Fake curl: simulates LoginUser, GET /api/runners, POST /api/runners/:id/upgrade, GET /api/runners/:id/upgrade.
cat > "$t/fake-curl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
method="GET"
url=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    -X) method="$2"; shift 2 ;;
    -H|--data-raw) shift 2 ;;
    -*) shift ;;
    *) url="$1"; shift ;;
  esac
done
echo "$method $url" >> "$FAKE_LOG"
case "$method $url" in
  "POST https://api.example.test/api/Account/LoginUser")
    echo '{"accessToken":"fake-token"}'
    ;;
  "GET https://api.example.test/api/runners")
    cat "$FAKE_RUNNERS_JSON"
    ;;
  "POST https://api.example.test/api/runners/r-1/upgrade")
    echo '{"runnerId":"r-1","state":"InProgress","message":"started"}'
    ;;
  "GET https://api.example.test/api/runners/r-1/upgrade")
    n=$(cat "$FAKE_POLL_COUNT" 2>/dev/null || echo 0)
    n=$((n + 1))
    echo "$n" > "$FAKE_POLL_COUNT"
    if [[ "${FAKE_ROLLBACK:-0}" == "1" ]]; then
      echo '{"runnerId":"r-1","state":"RolledBack","message":"failed to connect"}'
    elif [[ "$n" -lt 2 ]]; then
      echo '{"runnerId":"r-1","state":"InProgress","message":"restarting"}'
    else
      echo '{"runnerId":"r-1","state":"Reconnected","message":"reconnected"}'
    fi
    ;;
  *)
    echo "unexpected curl call: $method $url" >&2
    exit 1
    ;;
esac
EOF
chmod +x "$t/fake-curl"

cat > "$t/accounts.env" <<'EOF'
AUTOMATON_TEST_API_URL=https://api.example.test
AUTOMATON_TEST_ADMIN_EMAIL=admin@example.test
AUTOMATON_TEST_ADMIN_PASSWORD=secret$value
EOF

export UPGRADE_UPDATE_CMD="$t/fake-update.sh"
export UPGRADE_VERSION_FILE="$t/version"
export AUTOMATON_TEST_ACCOUNTS="$t/accounts.env"
export UPGRADE_CURL="$t/fake-curl"
export UPGRADE_POLL_INTERVAL=0
export UPGRADE_RECONNECT_WAIT=0
export UPGRADE_RUNNER_TIMEOUT=5
export FAKE_LOG="$t/log"
export FAKE_RUNNERS_JSON="$t/runners.json"
export FAKE_POLL_COUNT="$t/polls"

# 1. Invalid version fails with exit 2.
rc=0
bash "$d/upgrade-test-server.sh" "not a version" 2>/dev/null || rc=$?
[[ "$rc" -eq 2 ]] || { echo "FAIL: invalid version should exit 2, got $rc"; exit 1; }

# 2. Upgrades server and upgrades online runner with upgradeAvailable=true.
echo "server-v0.2.199" > "$t/version"
echo '[{"id":"r-1","name":"runner-01","status":"Online","upgradeAvailable":true},{"id":"r-2","name":"runner-02","status":"Offline","upgradeAvailable":true}]' > "$FAKE_RUNNERS_JSON"
: > "$FAKE_LOG"
rm -f "$FAKE_POLL_COUNT"
bash "$d/upgrade-test-server.sh" "0.2.200" >/dev/null
[[ "$(cat "$t/version")" == "server-v0.2.200" ]] || { echo "FAIL: version not updated"; exit 1; }
grep -qx "update --version server-v0.2.200" "$FAKE_LOG" || { echo "FAIL: update.sh not called"; exit 1; }
grep -qx "POST https://api.example.test/api/runners/r-1/upgrade" "$FAKE_LOG" || { echo "FAIL: runner upgrade not started"; exit 1; }
! grep -q "r-2" "$FAKE_LOG" || { echo "FAIL: offline runner should not be upgraded"; exit 1; }

# 3. Already on version skips update.sh and exits 0 when runners are current.
echo '[{"id":"r-1","name":"runner-01","status":"Online","upgradeAvailable":false}]' > "$FAKE_RUNNERS_JSON"
: > "$FAKE_LOG"
bash "$d/upgrade-test-server.sh" "server-v0.2.200" >/dev/null
! grep -q "^update " "$FAKE_LOG" || { echo "FAIL: update.sh should be skipped when already on version"; exit 1; }

# 4. RolledBack runner exits non-zero.
echo '[{"id":"r-1","name":"runner-01","status":"Online","upgradeAvailable":true}]' > "$FAKE_RUNNERS_JSON"
rm -f "$FAKE_POLL_COUNT"
rc=0
FAKE_ROLLBACK=1 bash "$d/upgrade-test-server.sh" "0.2.200" >/dev/null 2>&1 || rc=$?
[[ "$rc" -ne 0 ]] || { echo "FAIL: RolledBack runner should fail"; exit 1; }

echo "ok"
