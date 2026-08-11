#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HEALTH="$ROOT/scripts/agent-orchestrator-health.sh"
DEPLOY="$ROOT/scripts/deploy-agent-orchestrator.sh"
TEMP="$(mktemp -d)"
trap 'rm -rf -- "$TEMP"' EXIT
mkdir -p "$TEMP/bin"
if [[ -n "${TEST_PYTHON:-}" ]]; then
  printf '#!/usr/bin/env bash\nexec "%s" "$@"\n' "$TEST_PYTHON" > "$TEMP/bin/python3"
  chmod +x "$TEMP/bin/python3"
fi
cat > "$TEMP/bin/systemctl" <<'SH'
#!/usr/bin/env bash
[[ "$1" == "is-active" ]] && exit 0
exit 0
SH
chmod +x "$TEMP/bin/systemctl"
export PATH="$TEMP/bin:$PATH"
export AUTOMATON_AGENT_HEALTH_POLL_SECONDS=0.05
STATUS="$TEMP/status.json"
REVISION="0123456789abcdef0123456789abcdef01234567"

write_status() {
  printf '{"schemaVersion":1,"startupReconciled":true,"healthy":true,"applicationVersion":"%s","buildRevision":"%s"}\n' "$1" "$2" > "$STATUS"
}

expect_failure() {
  if "$@" >/dev/null 2>&1; then echo "Expected command to fail: $*" >&2; exit 1; fi
}

grep -q 'systemctl restart automaton-agent-orchestrator.service' "$DEPLOY"
grep -q 'systemctl enable automaton-agent-orchestrator.service' "$DEPLOY"
! grep -q 'enable --now' "$DEPLOY"
ELSE_BLOCK="$(sed -n '/^else$/,/^fi$/p' "$DEPLOY")"
! grep -Eq 'systemctl (start|restart)|enable --now' <<< "$ELSE_BLOCK"

write_status 1.0.0 "$REVISION"
expect_failure "$HEALTH" "$STATUS" 2.0.0 "$REVISION" 0
expect_failure "$HEALTH" "$STATUS" 1.0.0 ffffffffffffffffffffffffffffffffffffffff 0
"$HEALTH" "$STATUS" 1.0.0 "$REVISION" 0 >/dev/null

write_status 1.0.0 "$REVISION"
(sleep 0.2; write_status 2.0.0 "$REVISION") &
"$HEALTH" "$STATUS" 2.0.0 "$REVISION" 3 >/dev/null
wait
write_status 1.0.0 "$REVISION"
expect_failure "$HEALTH" "$STATUS" 2.0.0 "$REVISION" 1

! grep -Eq 'rm .*var/lib/automaton-agent-orchestrator|rm -r' "$DEPLOY"

if [[ -n "${PACKAGE_ARCHIVE:-}" ]]; then
  MANIFEST="$(tar -xOzf "$PACKAGE_ARCHIVE" ./build-manifest.json)"
  python3 - "$EXPECTED_PACKAGE_VERSION" "$EXPECTED_PACKAGE_REVISION" "$MANIFEST" <<'PY'
import json, sys
value = json.loads(sys.argv[3])
assert value == {
    "schemaVersion": 1,
    "applicationVersion": sys.argv[1],
    "buildRevision": sys.argv[2],
    "runtimeIdentifier": "linux-x64",
}
PY
fi

echo "agent orchestrator packaging tests passed"
