#!/usr/bin/env bash
# Test agent-finish-pr.sh with fake gh and merge-gate: only a PR failing on review/security starts a reviewer run.
set -euo pipefail
tools=$(cd "$(dirname "$0")" && pwd)
t=$(mktemp -d); trap 'rm -rf "$t"' EXIT
mkdir "$t/bin" "$t/tools"
cp "$tools/agent-finish-pr.sh" "$t/tools/"
printf '#!/bin/sh\necho "11 12 13 14"\n' > "$t/bin/gh"
chmod +x "$t/bin/gh"; cp "$t/bin/gh" "$t/bin/timeout"; printf '#!/bin/sh\nshift; shift\nexit 0\n' > "$t/bin/timeout"; chmod +x "$t/bin/timeout"
cat > "$t/tools/merge-gate.py" <<'PY'
import sys
pr = sys.argv[1]
if pr == "11": print("PASS  review: ok\nMERGE GATE: PASS"); sys.exit(0)
if pr == "12": print("FAIL  review: none\nFAIL  security: none\nMERGE GATE: FAIL"); sys.exit(1)
if pr == "14": print("Traceback"); sys.exit(1)
print("FAIL  ci: red\nFAIL  review: none\nMERGE GATE: FAIL"); sys.exit(1)
PY
printf '#!/bin/sh\necho "$PR_URL" >> "%s/started"\n' "$t" > "$t/tools/agent-run.sh"
chmod +x "$t/tools/agent-run.sh"
PATH="$t/bin:$PATH" AGENT_REPO=o/r NUMBER=5 bash "$t/tools/agent-finish-pr.sh" >/dev/null
[ "$(cat "$t/started")" = "https://github.com/o/r/pull/12" ] || { echo "FAIL: started $(cat "$t/started" 2>/dev/null)"; exit 1; }
echo "PASS"
