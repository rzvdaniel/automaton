#!/usr/bin/env bash
# Offline test with a fake runner. Run: bash scripts/agent-queue/test.sh
set -euo pipefail
d=$(dirname "$(readlink -f "$0")"); t=$(mktemp -d); trap 'rm -rf "$t"' EXIT
export AGENT_QUEUE_HOME=$t/q AGENT_QUEUE_BIN=$d/agent-queue
cat > "$t/fake" <<'F'
#!/usr/bin/env bash
p=$(cat); echo "start $1" >> "$AGENT_QUEUE_HOME/order"; echo "$p"
[[ $p == stall* ]] && sleep 600
sleep 1; echo "end $1" >> "$AGENT_QUEUE_HOME/order"
F
chmod +x "$t/fake"; export AGENT_QUEUE_RUNNER=$t/fake
fail() { echo "FAIL: $*"; exit 1; }
for x in a b c d; do echo $x | "$d/agent-queue" add $x; done
"$d/agent-queue" add a </dev/null 2>/dev/null && fail "duplicate accepted" || true
AGENT_QUEUE_ONCE=1 "$d/agent-queue" worker
[[ $(tr '\n' ' ' < "$t/q/order") == "start a end a start b end b start c end c start d end d " ]] || fail "order: $(cat "$t/q/order")"
echo stall | "$d/agent-queue" add s
( AGENT_QUEUE_ONCE=1 "$d/agent-queue" worker & ) ; sleep 3
[[ $("$d/agent-queue" status) == *"s (log idle"* ]] || fail "status shows no running task"
touch -d '2 hours ago' "$t/q/logs/s.log"
[[ $("$d/agent-queue" stalled 30) == s\ * ]] || fail "stall not detected"
[[ $(ORCH_MAX_RETRIES=0 "$d/orchestrator.sh") == *"failing it"* ]] || fail "orchestrator did not handle stall"
sleep 3; [[ $("$d/agent-queue" status) == *"s: cancelled"* ]] || fail "stalled task not cancelled"
echo "ALL PASS"
