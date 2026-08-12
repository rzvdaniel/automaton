#!/usr/bin/env bash
set -euo pipefail

SERVICE="automaton-agent-orchestrator.service"
STATUS="${1:-/var/lib/automaton-agent-orchestrator/workspaces/.automaton-agent-orchestrator/operator/status.json}"
EXPECTED_VERSION="${2:-}"
EXPECTED_REVISION="${3:-}"
TIMEOUT_SECONDS="${4:-${AUTOMATON_AGENT_HEALTH_TIMEOUT_SECONDS:-120}}"
POLL_SECONDS="${AUTOMATON_AGENT_HEALTH_POLL_SECONDS:-2}"
[[ -n "$EXPECTED_VERSION" && -n "$EXPECTED_REVISION" ]] || { echo "Expected version and revision are required." >&2; exit 2; }
[[ "$TIMEOUT_SECONDS" =~ ^[0-9]+$ && "$POLL_SECONDS" =~ ^[0-9]+([.][0-9]+)?$ ]] || { echo "Invalid health timeout or interval." >&2; exit 2; }

DEADLINE=$((SECONDS + TIMEOUT_SECONDS))
while (( SECONDS <= DEADLINE )); do
  if systemctl is-active --quiet "$SERVICE" && test -r "$STATUS" && python3 - "$STATUS" "$EXPECTED_VERSION" "$EXPECTED_REVISION" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as stream:
    value = json.load(stream)
required = value.get("schemaVersion") == 1 and value.get("startupReconciled") is True and value.get("healthy") is True
identity = value.get("applicationVersion") == sys.argv[2] and value.get("buildRevision") == sys.argv[3]
if not required or not identity:
    raise SystemExit(1)
PY
  then
    echo "healthy version=$EXPECTED_VERSION revision=$EXPECTED_REVISION"
    exit 0
  fi
  sleep "$POLL_SECONDS"
done
echo "$SERVICE did not reach healthy build $EXPECTED_VERSION ($EXPECTED_REVISION) within ${TIMEOUT_SECONDS}s" >&2
exit 1
