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
cat > "$t/gh" <<'F'
#!/usr/bin/env bash
case "$*" in *" 7 "*) printf 'true\nfalse\n';; *" 8 "*) printf 'false\nfalse\n';; *) printf 'false\ntrue\n';; esac
F
chmod +x "$t/gh"
AGENT_QUEUE_GH=$t/gh bash -c 'echo x | "$0" add impl-7' "$d/agent-queue" 2>/dev/null && fail "needs-shape issue queued" || true
AGENT_QUEUE_GH=$t/gh bash -c 'echo x | "$0" add impl-apps-8' "$d/agent-queue" 2>/dev/null && fail "issue without Done when queued" || true
AGENT_QUEUE_GH=$t/gh bash -c 'echo x | "$0" add impl-9' "$d/agent-queue" >/dev/null || fail "shaped issue refused"; "$d/agent-queue" cancel impl-9 >/dev/null
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
# pick on finish: fake gh holding a board (Ready: 20 shaped+ok, 21 unshaped, 22 shaped+fails, 23 shaped+ok)
b=$t/board; mkdir -p "$b"; for n in 20 21 22 23; do echo "Ready for agents" > "$b/$n"; done
cat > "$t/ghb" <<F
#!/usr/bin/env bash
b=$b; a="\$*"
case "\$a" in
  "project item-list"*) for n in 20 21 22 23; do printf 'ID%s\to/automaton\t%s\t%s\n' \$n \$n "\$(cat \$b/\$n)"; done;;
  "project view"*) echo PID;;
  "project field-list"*) case "\$a" in *Inbox*) printf 'FID\tOPT-Inbox\n';; *Running*) printf 'FID\tOPT-Running\n';; esac;;
  "project item-edit"*) id=\${a#*--id }; id=\${id%% *}; o=\${a##*option-id }; echo "\${o#OPT-}" > \$b/\${id#ID};;
  "issue view"*) n=\$3; [[ \$n == 21 ]] && printf 'true\ttrue\tnone\n' || printf 'false\ttrue\t%s\n' "\$(cat \$b/comment-\$n 2>/dev/null || echo none)";;
  "issue comment"*) n=\$3; echo "\$*" >> \$b/comments-\$n; date -u -d '+2 seconds' +%FT%TZ > \$b/comment-\$n;;
esac
F
chmod +x "$t/ghb"
cat > "$t/fake2" <<'F'
#!/usr/bin/env bash
cat >/dev/null; echo "run $1" >> "$AGENT_QUEUE_HOME/order2"; [[ $1 == pick-automaton-22 ]] && exit 3; exit 0
F
chmod +x "$t/fake2"
rm -rf "$t/q/pending"/* "$t/q/done"/* "$t/q/failed"/*; cat "$t/q/order2" 2>/dev/null || true
echo seed | "$d/agent-queue" add seed
AGENT_QUEUE_RUNNER=$t/fake2 AGENT_QUEUE_GH=$t/ghb AGENT_BOARD=o/9 AGENT_QUEUE_ONCE=1 "$d/agent-queue" worker
[[ $(tr '\n' ' ' < "$t/q/order2") == "run seed run pick-automaton-20 run pick-automaton-22 run pick-automaton-23 " ]] || fail "pick order: $(cat "$t/q/order2")"
[[ $(cat $b/21) == Inbox && $(wc -l < $b/comments-21) == 1 ]] || fail "unshaped card not sent back once"
[[ $(cat $b/22) == Inbox && $(cat $b/20) == Running ]] || fail "failed card not returned / ok card not Running"
grep -q "Last log lines" $b/comments-22 || fail "no log lines on failed card"
# failed card is skipped until a newer comment; unrelated Ready card is picked instead
echo "Ready for agents" > $b/22; echo "Ready for agents" > $b/23
echo seed | "$d/agent-queue" add seed2
( cd "$t"; AGENT_QUEUE_RUNNER=$t/fake2 AGENT_QUEUE_GH=$t/ghb AGENT_BOARD=o/9 AGENT_QUEUE_ONCE=1 "$d/agent-queue" worker )
tail -1 "$t/q/order2" | grep -q "pick-automaton-22" && fail "failed card picked again without a comment"
# the same name is refused for an hour after it finished (no double pick)
echo x | "$d/agent-queue" add pick-automaton-23 2>/dev/null && fail "recent pick re-queued" || true
AGENT_AUTOPICK=0 AGENT_QUEUE_RUNNER=$t/fake2 AGENT_QUEUE_GH=$t/ghb AGENT_BOARD=o/9 AGENT_QUEUE_ONCE=1 "$d/agent-queue" worker
# claude-task timeout
mkdir -p "$t/bin"; printf '#!/bin/sh\nsleep 30\n' > "$t/bin/claude"; chmod +x "$t/bin/claude"
git init -q "$t/ctw"; o=$(echo p | AGENT_WORKDIR=$t/ctw HOME=$t PATH=$t/bin:$PATH AGENT_TASK_TIMEOUT=2 "$d/claude-task" tt 2>&1 || true); [[ $o == *"timed out"* ]] || fail "claude-task did not time out: $o"
# agent-cleanup: removes merged clean worktrees, keeps unpushed/dirty/open ones
export AGENT_CLEAN_REPOS=$t/repo GH=$t/gh
git init -q --bare "$t/origin.git"; git init -q -b main "$t/repo"; cd "$t/repo"
git config user.email t@t; git config user.name t; git remote add origin "$t/origin.git"
git commit -q --allow-empty -m init; git push -q origin main
for b in merged dirty unpushed open; do git worktree add -q -b $b "$t/wt-$b"; git -C "$t/wt-$b" commit -q --allow-empty -m $b; git -C "$t/wt-$b" push -q origin $b; done
echo x > "$t/wt-dirty/new.txt"
git -C "$t/wt-unpushed" commit -q --allow-empty -m local
cat > "$t/gh" <<G
#!/usr/bin/env bash
b=\$4; sha=\$(git --git-dir=$t/origin.git rev-parse "refs/heads/\$b")
[[ \$b == open ]] && echo "OPEN \$sha" || echo "MERGED \$sha"
G
chmod +x "$t/gh"
git -C "$t/repo" worktree add -q -b busy "$t/wt-busy"; git -C "$t/wt-busy" commit -q --allow-empty -m busy; git -C "$t/wt-busy" push -q origin busy
(cd "$t/wt-busy" && exec sleep 30) & busy=$!
sleep 1; out=$("$d/agent-cleanup" worktrees); cd "$d"; kill $busy
[[ -d $t/wt-busy ]] || fail "worktree in use was removed"
[[ ! -d $t/wt-merged ]] || fail "merged worktree kept: $out"
for b in dirty unpushed open; do [[ -d $t/wt-$b ]] || fail "$b worktree removed"; done
[[ $out == *"kept $t/wt-unpushed: HEAD is not"* ]] || fail "unpushed reason: $out"
AGENT_CLEAN_DISK_PATH=$t AGENT_CLEAN_MIN_GIB=0 "$d/agent-cleanup" caches | grep -q "left alone" || fail "caches cleared with enough space"
[[ $("$d/agent-cleanup" disk) == "free disk: "*" GiB"* ]] || fail "disk line"
echo "ALL PASS"
