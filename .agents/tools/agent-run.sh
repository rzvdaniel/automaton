#!/usr/bin/env bash
# Run one Claude prompt on an agent machine (called by the agent workflows).
# Env: PROMPT_FILE (repo path), AGENT_REPO (owner/repo of the issue; its checkout ~/<repo> is the work base), ISSUE_URL (replaces ISSUE_URL in the prompt), AGENT_TASK_TIMEOUT (default 3h),
# AGENT_MODEL (sonnet), AGENT_EFFORT (low), CLAUDE_CODE_OAUTH_TOKEN (never printed).
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
prompt=$(cat "$prompt_base/$PROMPT_FILE"); prompt=${prompt//ISSUE_URL/${ISSUE_URL:-}}
set +e
timeout -k 30 "${AGENT_TASK_TIMEOUT:-3h}" claude -p "$prompt" --model "${AGENT_MODEL:-sonnet}" --effort "${AGENT_EFFORT:-low}" --dangerously-skip-permissions < /dev/null
rc=$?
[ "$rc" = 124 ] && echo "agent-run: timed out after ${AGENT_TASK_TIMEOUT:-3h}; stopped" >&2
exit "$rc"
