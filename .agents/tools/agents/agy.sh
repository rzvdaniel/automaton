#!/usr/bin/env bash
# Agent tool adapter: Antigravity (agy). Contract: run <model> <prompt> (headless, unattended; the effort is part of
# the model id, e.g. gemini-4-hydrogen-high, and agy refuses --effort with it),
# models (one "id<TAB>label" per line), check (exit 0: installed and signed in).
set -uo pipefail
export PATH=$HOME/.local/bin:$PATH
case "${1:-}" in
  run)
    # agy cannot start subagents: the finish step (agent-finish-pr.sh) posts the reviews with the runner's other logins.
    # agy never sees the Claude token.
    exec env -u CLAUDE_CODE_OAUTH_TOKEN agy -p "$3

You cannot start subagents in this run. Do every step of the job except the reviews and the security pass: open the pull request with its description and stop there. The next step of this job posts the reviews and merges through the gate." \
      --model "$2" --dangerously-skip-permissions < /dev/null 2>&1 ;;
  models) agy models 2>/dev/null | grep -P '^[^\t ]+\t' ;;
  check) command -v agy >/dev/null && [ -n "$(agy models 2>/dev/null | grep -P '^[^\t ]+\t')" ] ;;
  *) echo "usage: agy.sh run <model> <prompt> | models | check" >&2; exit 2 ;;
esac
