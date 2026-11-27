#!/usr/bin/env bash
# Run one agent prompt on an agent machine (called by the agent workflows), with Claude Code or Antigravity (agy).
# Env: PROMPT_FILE (repo path), PR_URL (replaces PR_URL in the prompt), AGENT_REPO (owner/repo of the issue; its checkout ~/<repo> is the work base), ISSUE_URL (replaces ISSUE_URL in the prompt), AGENT_TASK_TIMEOUT (default 3h),
# AGENT_CLI (claude, or agy), AGENT_MODEL (sonnet for claude, gemini-4-hydrogen-high for agy), AGENT_EFFORT (low), CLAUDE_CODE_OAUTH_TOKEN (never printed),
# AGENT_RECORD=1: post one line on the issue with the tool, model, minutes and whether the Claude fallback ran (#1864).
# With agy, a failed run, a usage limit or a run that leaves no pull request for the issue runs once more on claude.
set -euo pipefail
export PATH=$HOME/.local/bin:$HOME/.dotnet/tools:$PATH DOTNET_ROOT=$HOME/.dotnet
unset CLAUDE_CONFIG_DIR
# The shared checkout stays on main: it holds this script and agent-board.sh. Each run works in its own worktree.
base="${AGENT_WORKDIR:-$HOME/automaton}"
prompt_base="$base"
repo_name="${AGENT_REPO:-}"; repo_name="${repo_name##*/}"
if [ -n "$repo_name" ] && [ "$repo_name" != automaton ]; then
  [[ $repo_name =~ ^[A-Za-z0-9._-]+$ && $repo_name != .* && -d "$HOME/$repo_name/.git" ]]     || { echo "agent-run: no checkout ~/$repo_name for $AGENT_REPO" >&2; exit 1; }
  base="$HOME/$repo_name"
fi
work_root="${AGENT_WORK_ROOT:-$HOME/agent-work}"
cd "$base"
git fetch -q origin main
# put the shared checkout back on main and fast-forward it, only when it is clean
if [ -z "$(git status --porcelain --untracked-files=no)" ]; then
  git checkout -q main && git merge -q --ff-only origin/main || echo "agent-run: could not fast-forward main" >&2
else
  echo "agent-run: shared checkout is not a clean main; leaving it alone" >&2
fi
# prune stale worktrees from earlier runs (the agent queue runs one job at a time)
mkdir -p "$work_root"
for d in "$work_root"/*/; do [ -d "$d" ] && { git worktree remove --force "$d" 2>/dev/null || rm -rf "$d"; }; done
git worktree prune
wt="$work_root/${GITHUB_RUN_ID:-$$}-${GITHUB_RUN_ATTEMPT:-1}"
git worktree add -q --detach "$wt" origin/main
cleanup() { cd "$base" && { git worktree remove --force "$wt" 2>/dev/null || rm -rf "$wt"; git worktree prune; }; }
trap cleanup EXIT
cd "$wt"
# Another repository's worktree has no Automaton agent definitions or skills: bring them from the shared checkout
# (copied only where missing, and removed with the worktree) so reviews and implementers start there too.
if [ "$base" != "$prompt_base" ]; then
  mkdir -p .claude/agents .agents/skills
  cp -rn "$prompt_base/.claude/agents/." .claude/agents/ 2>/dev/null || true
  cp -rn "$prompt_base/.agents/skills/." .agents/skills/ 2>/dev/null || true
  # never commit the copies: list each untracked one in this repository's exclude file
  exclude=$(git rev-parse --git-path info/exclude); mkdir -p "$(dirname "$exclude")"
  git ls-files --others --exclude-standard .claude/agents .agents/skills | sed 's|^|/|' >> "$exclude"; sort -u -o "$exclude" "$exclude"
fi
prompt=$(cat "$prompt_base/$PROMPT_FILE"); prompt=${prompt//ISSUE_URL/${ISSUE_URL:-}}; prompt=${prompt//PR_URL/${PR_URL:-}}
cli=${AGENT_CLI:-claude}; [ "$cli" = agy ] || cli=claude
timeout_after=${AGENT_TASK_TIMEOUT:-3h}
issue_number=${ISSUE_URL:-}; issue_number=${issue_number##*/}; [[ $issue_number =~ ^[0-9]+$ ]] || issue_number=
# run_cli <claude|agy> <model>: one run in this worktree; returns the CLI's exit code (124 = timed out)
run_cli() {
  if [ "$1" = agy ]; then
    # agy cannot start subagents: the finish step (agent-finish-pr.sh) posts the reviews with the runner's other logins.
    timeout -k 30 "$timeout_after" agy -p "$prompt

You cannot start subagents in this run. Do every step of the job except the reviews and the security pass: open the pull request with its description and stop there. The next step of this job posts the reviews and merges through the gate."       --model "$2" --effort "${AGENT_EFFORT:-low}" --dangerously-skip-permissions < /dev/null 2>&1
    return

  fi
  timeout -k 30 "$timeout_after" claude -p "$prompt" --model "$2" --effort "${AGENT_EFFORT:-low}" --dangerously-skip-permissions < /dev/null
}
# has_pr: an open or merged pull request references the issue (unknown issue: assume yes)
has_pr() {
  [ -n "$issue_number" ] || return 0
  local n
  n=$(gh pr list -R "${AGENT_REPO:-rzvdaniel/automaton}" --state all --limit 100 --search "$issue_number in:title,body" --json state,title,body     --jq "[.[] | select(.state != \"CLOSED\") | select((.title + \" \" + .body) | test(\"#$issue_number([^0-9]|\$)\"))] | length" 2>/dev/null) || return 0
  [ "${n:-0}" -gt 0 ]
}
started=$(date +%s); fallback=no
if [ "$cli" = agy ]; then model=${AGENT_MODEL:-gemini-4-hydrogen-high}; else model=${AGENT_MODEL:-sonnet}; fi
set +e
run_cli "$cli" "$model"; rc=$?
# A usage limit ends agy with an error, or with no pull request; the log's wording is not trusted (it echoes the task).
if [ "$cli" = agy ] && { [ "$rc" != 0 ] || ! has_pr; }; then
  echo "agent-run: agy did not finish the job (exit $rc); running it again on claude" >&2
  git checkout -q -f --detach origin/main && git clean -qfdx
  fallback=yes; cli=claude; model=${AGENT_FALLBACK_MODEL:-sonnet}
  run_cli claude "$model"; rc=$?
fi
[ "$rc" = 124 ] && echo "agent-run: timed out after $timeout_after; stopped" >&2
if [ "${AGENT_RECORD:-}" = 1 ] && [ -n "$issue_number" ]; then
  minutes=$(( ($(date +%s) - started + 59) / 60 ))
  gh issue comment "$issue_number" -R "${AGENT_REPO:-rzvdaniel/automaton}"     --body "Agent run: tool $cli, model $model, $minutes min, fallback: $fallback, exit $rc." >/dev/null 2>&1 || true
fi
exit "$rc"
