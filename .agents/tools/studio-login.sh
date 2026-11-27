#!/usr/bin/env bash
# Sign in to Studio in a headless agent-browser session without printing credentials.
# Usage: studio-login.sh <session> [admin|user]   (reads ~/.config/automaton-test/accounts.env literally; test server only)
set -euo pipefail
session=${1:?session name}; role=${2:-admin}
env_file=${AUTOMATON_TEST_ACCOUNTS:-$HOME/.config/automaton-test/accounts.env}
get() { grep -m1 "^$1=" "$env_file" | cut -d= -f2- | tr -d '\r'; }
url=${AUTOMATON_TEST_STUDIO_URL:-$(get AUTOMATON_TEST_STUDIO_URL)}   # prefer the real Studio domain (https://studio.runner-01.automaton.monster)
[ "$role" = admin ] && key=ADMIN || key=USER
email=$(get "AUTOMATON_TEST_${key}_EMAIL"); pass=$(get "AUTOMATON_TEST_${key}_PASSWORD")
ab() { agent-browser --session "$session" "$@"; }
ab open "$url" >/dev/null
ab wait 2500 >/dev/null
if ab is visible 'input[type=password]' 2>/dev/null | grep -q true; then
  ab fill 'input[type=email], input[name=email]' "$email" >/dev/null
  ab fill 'input[type=password]' "$pass" >/dev/null
  ab press Enter >/dev/null
  ab wait 3000 >/dev/null
fi
echo "signed in as $role: $(ab get url)"
