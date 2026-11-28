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
  if [ "${AGENT_REVIEW_CLI:-claude}" = claude ]; then
    # Claude Code starts one automaton-reviewer subagent per missing piece and merges through the gate.
    PROMPT_FILE=.agents/jobs/finish-pr.md AGENT_REPO="$repo" AGENT_CLI=claude AGENT_MODEL=${AGENT_REVIEW_MODEL:-sonnet} \
      ISSUE_URL="https://github.com/$repo/issues/$number" \
      PR_URL="https://github.com/$repo/pull/$pr" bash "$tools/agent-run.sh" || rc=$?
    continue
  fi
  # Tools without subagents (agy): each missing piece is its own fresh run, so the two reviews stay independent;
  # this script merges through the gate afterwards (a CHANGES NEEDED review keeps the gate red).
  for piece in review security; do
    grep -q -E "^FAIL +$piece:" <<<"$out" || continue
    job=review-main.md; [ "$piece" = security ] && job=review-security.md
    PROMPT_FILE=.agents/jobs/$job AGENT_REPO="$repo" AGENT_CLI="$AGENT_REVIEW_CLI" AGENT_MODEL=${AGENT_REVIEW_MODEL:-gemini-4-neon-high} \
      ISSUE_URL="https://github.com/$repo/issues/$number" \
      PR_URL="https://github.com/$repo/pull/$pr" bash "$tools/agent-run.sh" || rc=$?
  done
  if python3 "$tools/merge-gate.py" "$pr" --repo "$repo" --merge; then
    gh issue comment "$number" -R "$repo" --body "Reviews posted by $AGENT_REVIEW_CLI (${AGENT_REVIEW_MODEL:-gemini-4-neon-high}); PR #$pr merged through the gate." >/dev/null 2>&1 || true
  else
    gh issue comment "$number" -R "$repo" --body "Reviews by $AGENT_REVIEW_CLI (${AGENT_REVIEW_MODEL:-gemini-4-neon-high}) done; PR #$pr not merged: the merge gate still fails (see the PR)." >/dev/null 2>&1 || true
  fi
done
exit $rc
