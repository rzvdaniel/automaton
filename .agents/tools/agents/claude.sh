#!/usr/bin/env bash
# Agent tool adapter: Claude Code. Contract: run <model> <prompt> (headless, unattended; env AGENT_EFFORT),
# models (one "id<TAB>label" per line), check (exit 0: installed and signed in). Add a tool = add one file like this.
set -uo pipefail
export PATH=$HOME/.local/bin:$HOME/.dotnet/tools:$PATH
case "${1:-}" in
  run) exec claude -p "$3" --model "$2" --effort "${AGENT_EFFORT:-low}" --dangerously-skip-permissions < /dev/null ;;
  models) printf 'sonnet\tSonnet\nopus\tOpus\nhaiku\tHaiku\n' ;;
  check) command -v claude >/dev/null && claude auth status 2>/dev/null | grep -q '"loggedIn": true' ;;
  *) echo "usage: claude.sh run <model> <prompt> | models | check" >&2; exit 2 ;;
esac
