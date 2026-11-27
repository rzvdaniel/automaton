#!/usr/bin/env bash
# After an implementer run: for each open PR of the issue whose merge gate fails only on review/security,
# start a reviewer run (finish-pr.md) that posts the missing reviews with the runner's other logins and merges.
# Env: AGENT_REPO (owner/repo), NUMBER (issue number). Exits 0 when there is nothing to finish.
set -uo pipefail
tools=$(cd "$(dirname "$0")" && pwd)
repo=${AGENT_REPO:?}; number=${NUMBER:?}
# Only PRs by the owner or the bot that say Part of/Refs/Closes/Fixes #N (anyone else's PR is never reviewed or merged here).
prs=$(gh pr list -R "$repo" --state open --limit 50 --json number,body,author \
  -q ".[] | select(.author.login == \"rzvdaniel\" or .author.login == \"rzvdaniel-bot\") | select(.body | test(\"(Part of|Refs|Closes|Fixes) #$number([^0-9]|\$)\")) | .number") || exit 0
rc=0
for pr in $prs; do
  # CI on a new PR is usually still running; the gate fails on an empty or pending list.
  timeout 30m gh pr checks "$pr" -R "$repo" --watch >/dev/null 2>&1 || true
  out=$(python3 "$tools/merge-gate.py" "$pr" --repo "$repo" 2>&1) && continue
  # Start reviewers only when review and/or security fail and nothing else does (a traceback has no FAIL rows).
  grep -q -E '^FAIL +(review|security):' <<<"$out" || { echo "agent-finish-pr: PR $pr: not a review gap; leaving it"; continue; }
  if grep '^FAIL ' <<<"$out" | grep -qv -E '^FAIL +(review|security):'; then
    echo "agent-finish-pr: PR $pr fails on more than reviews; leaving it"; continue
  fi
  echo "agent-finish-pr: PR $pr lacks only review/security; starting reviewers"
  # Reviews start automaton-reviewer subagents, which only Claude Code has: always claude, whatever AGENT_CLI says.
  PROMPT_FILE=.agents/jobs/finish-pr.md AGENT_REPO="$repo" AGENT_CLI=claude AGENT_MODEL=${AGENT_REVIEW_MODEL:-sonnet} \
    ISSUE_URL="https://github.com/$repo/issues/$number" \
    PR_URL="https://github.com/$repo/pull/$pr" bash "$tools/agent-run.sh" || rc=$?
done
exit $rc
