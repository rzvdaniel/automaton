#!/usr/bin/env bash
# Test agent-run.sh with fake claude/agy/gh and a temp repo: shared checkout stays on main, worktree removed after the run.
set -euo pipefail
unset AGENT_CLI AGENT_MODEL AGENT_REPO AGENT_RECORD AGENT_EFFORT AGENT_FALLBACK_MODEL AGENT_TASK_TIMEOUT AGENT_AGY_TIMEOUT ISSUE_URL PR_URL
tools=$(cd "$(dirname "$0")" && pwd)
t=$(mktemp -d); trap 'rm -rf "$t"' EXIT
git init -q --bare "$t/origin.git"
git clone -q "$t/origin.git" "$t/base" 2>/dev/null
cd "$t/base"; git checkout -q -b main
git -c user.name=t -c user.email=t@t commit -q --allow-empty -m one; echo p > prompt.md; git add .
git -c user.name=t -c user.email=t@t commit -q -m two; git push -q origin main
git checkout -q -b feature   # a previous job left the checkout on a branch
mkdir "$t/bin" "$t/work"; mkdir "$t/work/stale"
printf '#!/bin/sh\npwd > "%s/cwd"\ngit checkout -q -b job-branch\n' "$t" > "$t/bin/claude"; chmod +x "$t/bin/claude"
HOME="$t/home" PATH="$t/bin:$PATH" AGENT_WORKDIR="$t/base" AGENT_WORK_ROOT="$t/work" PROMPT_FILE=prompt.md GITHUB_RUN_ID=7 bash "$tools/agent-run.sh"
[ "$(cat "$t/cwd")" = "$t/work/7-1" ] || { echo "FAIL: ran in $(cat "$t/cwd")"; exit 1; }
[ "$(git -C "$t/base" branch --show-current)" = main ] || { echo "FAIL: shared checkout not on main"; exit 1; }
[ -z "$(ls "$t/work")" ] || { echo "FAIL: leftover worktree"; exit 1; }
[ "$(git -C "$t/base" worktree list | wc -l)" = 1 ] || { echo "FAIL: worktree registered"; exit 1; }
# an issue from another repository runs in a worktree of ~/<repo>, with the prompt from the automaton checkout,
# and gets the Automaton agents and skills it lacks (its own files win, and the copies are never seen by git)
mkdir -p "$t/base/.claude/agents" "$t/base/.agents/skills/s1"
echo ours > "$t/base/.claude/agents/automaton-reviewer.md"; echo ours > "$t/base/.claude/agents/shared.md"; echo skill > "$t/base/.agents/skills/s1/SKILL.md"
git init -q --bare "$t/other.git"; git clone -q "$t/other.git" "$t/home/other" 2>/dev/null
cd "$t/home/other"; git checkout -q -b main; mkdir -p .claude/agents; echo theirs > .claude/agents/shared.md; git add .
git -c user.name=t -c user.email=t@t commit -q -m one; git push -q origin main; cd "$t/base"
printf '#!/bin/sh
pwd > "%s/cwd"
cat .claude/agents/automaton-reviewer.md .claude/agents/shared.md .agents/skills/s1/SKILL.md > "%s/seen" 2>&1
git status --porcelain > "%s/status"
' "$t" "$t" "$t" > "$t/bin/claude"
HOME="$t/home" PATH="$t/bin:$PATH" AGENT_WORKDIR="$t/base" AGENT_WORK_ROOT="$t/work" PROMPT_FILE=prompt.md AGENT_REPO=o/other GITHUB_RUN_ID=8 bash "$tools/agent-run.sh"
[ "$(cat "$t/cwd")" = "$t/work/8-1" ] || { echo "FAIL: other repo ran in $(cat "$t/cwd")"; exit 1; }
[ "$(cat "$t/seen" | tr '
' ' ')" = "ours theirs skill " ] || { echo "FAIL: agents/skills not copied right: $(cat "$t/seen")"; exit 1; }
[ ! -s "$t/status" ] || { echo "FAIL: copies show in git status: $(cat "$t/status")"; exit 1; }
[ "$(git -C "$t/home/other" worktree list | wc -l)" = 1 ] || { echo "FAIL: other repo worktree left"; exit 1; }
[ "$(git -C "$t/base" worktree list | wc -l)" = 1 ] || { echo "FAIL: automaton worktree used for other repo"; exit 1; }
# #1871: a run for another repository fast-forwards the automaton checkout and re-runs a changed agent-run.sh
mkdir -p "$t/base/.agents/tools"; cp -r "$tools/agent-run.sh" "$tools/agents" "$t/base/.agents/tools/"
git -C "$t/base" add -f .; git -C "$t/base" -c user.name=t -c user.email=t@t commit -q -m tools; git -C "$t/base" push -q origin main
git clone -q -b main "$t/origin.git" "$t/upd" 2>/dev/null; cd "$t/upd"
{ cat "$tools/agent-run.sh"; echo '# new version marker'; } > .agents/tools/agent-run.sh
echo newprompt > prompt.md
git -c user.name=t -c user.email=t@t commit -q -am newer; git push -q origin main; cd "$t/base"
printf '#!/bin/sh
cat "%s/base/.agents/tools/agent-run.sh" | grep -c "new version marker" > "%s/marker"
' "$t" "$t" > "$t/bin/claude"
HOME="$t/home" PATH="$t/bin:$PATH" AGENT_WORKDIR="$t/base" AGENT_WORK_ROOT="$t/work" PROMPT_FILE=prompt.md AGENT_REPO=o/other GITHUB_RUN_ID=10 bash "$t/base/.agents/tools/agent-run.sh"
[ "$(git -C "$t/base" rev-parse HEAD)" = "$(git -C "$t/base" rev-parse origin/main)" ] || { echo "FAIL: automaton checkout not fast-forwarded"; exit 1; }
[ "$(cat "$t/marker")" -ge 1 ] || { echo "FAIL: new script not present"; exit 1; }
# AGENT_CLI=agy (#1864): agy runs with its default model; a failed agy run, or one that leaves no PR, reruns once on claude,
# from a clean worktree, and AGENT_RECORD posts the tool line on the issue.
printf '#!/bin/sh
echo "agy $*" >> "%s/calls"
touch agy-left-this
exit "${FAKE_AGY_RC:-0}"
' "$t" > "$t/bin/agy"
printf '#!/bin/sh
echo "claude $*" | cut -c1-40 >> "%s/calls"
[ -e agy-left-this ] && echo dirty >> "%s/calls"
exit 0
' "$t" "$t" > "$t/bin/claude"
printf '#!/bin/sh
case "$1 $2" in "pr list") echo "${FAKE_PRS:-1}";; "issue comment") echo "$*" >> "%s/comments";; esac
' "$t" > "$t/bin/gh"
chmod +x "$t/bin/agy" "$t/bin/claude" "$t/bin/gh"
run_agy() { rm -f "$t/calls" "$t/comments"; HOME="$t/home" PATH="$t/bin:$PATH" AGENT_WORKDIR="$t/base" AGENT_WORK_ROOT="$t/work" PROMPT_FILE=prompt.md   AGENT_CLI=agy AGENT_RECORD=1 ISSUE_URL=https://github.com/o/automaton/issues/42 GITHUB_RUN_ID=9 "$@" bash "$tools/agent-run.sh"; }
run_agy env FAKE_AGY_RC=0 FAKE_PRS=1
grep -q -- "--model gemini-4-hydrogen-high" "$t/calls" || { echo "FAIL: agy model: $(cat "$t/calls")"; exit 1; }
! grep -q '^claude' "$t/calls" || { echo "FAIL: claude ran after a good agy run"; exit 1; }
grep -q "tool agy, model gemini-4-hydrogen-high, .* fallback: no" "$t/comments" || { echo "FAIL: record: $(cat "$t/comments")"; exit 1; }
run_agy env FAKE_AGY_RC=1 FAKE_PRS=1
grep -q '^claude' "$t/calls" || { echo "FAIL: no claude fallback after agy failed"; exit 1; }
! grep -q dirty "$t/calls" || { echo "FAIL: claude saw agy's leftovers"; exit 1; }
grep -q "tool claude, model sonnet, .* fallback: yes" "$t/comments" || { echo "FAIL: fallback record: $(cat "$t/comments")"; exit 1; }
run_agy env FAKE_AGY_RC=0 FAKE_PRS=0
grep -q '^claude' "$t/calls" || { echo "FAIL: no claude fallback when agy left no PR"; exit 1; }
# the claude fallback in another repository still has the Automaton agents and skills (clean -x removed the copies)
printf '#!/bin/sh
echo "claude" >> "%s/calls"
[ -f .claude/agents/automaton-reviewer.md ] && [ -f .agents/skills/s1/SKILL.md ] && echo agents-ok >> "%s/calls"
exit 0
' "$t" "$t" > "$t/bin/claude"
run_agy env FAKE_AGY_RC=1 FAKE_PRS=1 AGENT_REPO=o/other
grep -q agents-ok "$t/calls" || { echo "FAIL: fallback in another repo lost the agents: $(cat "$t/calls")"; exit 1; }
echo "PASS"
