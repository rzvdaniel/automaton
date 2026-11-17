#!/usr/bin/env bash
# Offline test with a fake runner. Run: bash scripts/agent-queue/test.sh
set -euo pipefail
d=$(dirname "$(readlink -f "$0")"); t=$(mktemp -d); trap 'rm -rf "$t"' EXIT
export AGENT_QUEUE_HOME=$t/q AGENT_QUEUE_BIN=$d/agent-queue
cat > "$t/fake" <<'F'
#!/usr/bin/env bash
p=$(cat); echo "start $1" >> "$AGENT_QUEUE_HOME/order"; echo "$p"
[[ $p == *$'\nstall'* ]] && sleep 600
sleep 1; echo "end $1" >> "$AGENT_QUEUE_HOME/order"
F
chmod +x "$t/fake"; export AGENT_QUEUE_RUNNER=$t/fake
fail() { echo "FAIL: $*"; exit 1; }
for x in a b c d; do echo $x | "$d/agent-queue" add $x; done
echo again | "$d/agent-queue" add a 2>/dev/null && fail "duplicate accepted" || true
echo x | "$d/agent-queue" add x-e; echo y | "$d/agent-queue" add e; "$d/agent-queue" cancel e >/dev/null; [[ -n $(ls "$t"/q/pending/*-x-e.task) && -z $(ls "$t"/q/pending/*-e.task 2>/dev/null | grep -v x-e) ]] || fail "cancel e hit x-e"; "$d/agent-queue" cancel x-e >/dev/null
AGENT_QUEUE_ONCE=1 "$d/agent-queue" worker
[[ $(tr '\n' ' ' < "$t/q/order") == "start a end a start b end b start c end c start d end d " ]] || fail "order: $(cat "$t/q/order")"
echo stall | "$d/agent-queue" add s
( AGENT_QUEUE_ONCE=1 "$d/agent-queue" worker & ) ; sleep 3
[[ $("$d/agent-queue" status) == *"s (log idle"* ]] || fail "status shows no running task"
touch -d '2 hours ago' "$t/q/logs/s.log"
[[ $("$d/agent-queue" stalled 30) == s\ * ]] || fail "stall not detected"
[[ $(ORCH_MAX_RETRIES=0 "$d/orchestrator.sh") == *"failing it"* ]] || fail "orchestrator did not handle stall"
sleep 3; [[ $("$d/agent-queue" status) == *"s: cancelled"* ]] || fail "stalled task not cancelled"
echo hello | "$d/agent-queue" add p >/dev/null; head -c 400 "$t"/q/pending/*-p.task | grep -q "^Rules: follow AGENTS.md" || fail "no rules prefix"; grep -qx hello "$t"/q/pending/*-p.task || fail "prompt not on its own line"; grep -q "handoff.nn" "$t"/q/pending/*-p.task && fail "literal nn in prompt"
eval "$(sed -n '/^limit_reset() {/,/^}/p' "$d/agent-queue")"
now=$(date +%s); ft="11am"; r=$(limit_reset "You've hit your limit · resets $ft (UTC)")
(( r > now && r <= now + 86400 )) || fail "limit_reset 11am: $r"; [[ $(date -u -d @"$r" +%H:%M) == 11:00 ]] || fail "limit_reset not 11:00 UTC"
past=$(date -u -d '2 hours ago' +%-I%P); r=$(limit_reset "hit your limit resets $past (UTC)"); (( r > now )) || fail "past reset not moved to next day"
r=$(limit_reset "hit your limit resets soonish"); (( r >= now + 3590 && r <= now + 3700 )) || fail "unreadable reset not +1h: $r"
eval "$(sed -n '/^limit_deadline() {/,/^}/p' "$d/agent-queue")"
r=$(limit_deadline "hit your limit resets $past (UTC)"); (( r <= now + 21605 )) || fail "wait not capped at 6h: $((r-now))"
r=$(limit_deadline "hit your limit resets soonish"); (( r >= now + 3890 && r <= now + 4000 )) || fail "short wait changed: $((r-now))"
# the continue note is appended only once
n='Note from agent-queue: the previous run stopped at the usage limit.'
echo task > "$t/lt"; for i in 1 2; do grep -qF "$n" "$t/lt" || printf '\n\n%s more\n' "$n" >> "$t/lt"; done
[[ $(grep -cF "$n" "$t/lt") == 1 ]] || fail "note duplicated"
echo "ALL PASS"
