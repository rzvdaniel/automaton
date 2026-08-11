#!/usr/bin/env bash
set -euo pipefail

SERVICE="automaton-agent-orchestrator.service"
STATUS="${1:-/var/lib/automaton-agent-orchestrator/workspaces/.automaton-agent-orchestrator/operator/status.json}"
systemctl is-active --quiet "$SERVICE" || { echo "$SERVICE is not active" >&2; exit 1; }
test -r "$STATUS" || { echo "Operator status is not readable: $STATUS" >&2; exit 1; }
python3 - "$STATUS" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as stream:
    value = json.load(stream)
required = value.get("schemaVersion") == 1 and value.get("startupReconciled") is True and value.get("healthy") is True
identity = bool(value.get("applicationVersion")) and bool(value.get("buildRevision"))
if not required or not identity:
    raise SystemExit("operator status is not reconciled/healthy or lacks build identity")
print(f"healthy version={value['applicationVersion']} revision={value['buildRevision']} state={value.get('daemonState')}")
PY
