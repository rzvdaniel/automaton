#!/usr/bin/env bash
# Run one Claude prompt on an agent machine (called by the agent workflows).
# Env: PROMPT_FILE (repo path), ISSUE_URL (replaces ISSUE_URL in the prompt), AGENT_TASK_TIMEOUT (default 3h),
# AGENT_MODEL (sonnet), AGENT_EFFORT (low), CLAUDE_CODE_OAUTH_TOKEN (never printed).
set -euo pipefail
export PATH=$HOME/.local/bin:$HOME/.dotnet/tools:$PATH DOTNET_ROOT=$HOME/.dotnet
unset CLAUDE_CONFIG_DIR
cd "${AGENT_WORKDIR:-$HOME/automaton}"
git fetch -q origin main && git checkout -q main && git merge -q --ff-only origin/main || echo "agent-run: staying on $(git rev-parse --short HEAD)" >&2
prompt=$(cat "$PROMPT_FILE"); prompt=${prompt//ISSUE_URL/${ISSUE_URL:-}}
set +e
timeout -k 30 "${AGENT_TASK_TIMEOUT:-3h}" claude -p "$prompt" --model "${AGENT_MODEL:-sonnet}" --effort "${AGENT_EFFORT:-low}" --dangerously-skip-permissions < /dev/null
rc=$?
[ "$rc" = 124 ] && echo "agent-run: timed out after ${AGENT_TASK_TIMEOUT:-3h}; stopped" >&2
exit "$rc"
