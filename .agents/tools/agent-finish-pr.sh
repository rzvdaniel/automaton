#!/usr/bin/env bash
# After an implementer run: for each open PR of the issue whose merge gate fails only on review/security,
# start a reviewer run (finish-pr.md) that posts the missing reviews with the runner's other logins and merges.
# Env: AGENT_REPO (owner/repo), NUMBER (issue number). Exits 0 when there is nothing to finish.
set -uo pipefail
tools=$(cd "$(dirname "$0")" && pwd)
repo=${AGENT_REPO:?}; number=${NUMBER:?}
prs=$(gh pr list -R "$repo" --state open --limit 50 --json number,url,body,headRefName \
  -q ".[] | select((.body | test(\"(Part of|Refs|Closes|Fixes)? ?#$number([^0-9]|\$)\")) or (.headRefName | test(\"(^|[^0-9])$number([^0-9]|\$)\"))) | .number") || exit 0
rc=0
for pr in $prs; do
  out=$(python3 "$tools/merge-gate.py" "$pr" --repo "$repo" 2>&1) && continue
  fails=$(grep '^FAIL ' <<<"$out" | grep -v -E '^FAIL +(review|security):' || true)
  if [ -n "$fails" ]; then echo "agent-finish-pr: PR $pr fails on more than reviews; leaving it"; continue; fi
  echo "agent-finish-pr: PR $pr lacks only review/security; starting reviewers"
  PROMPT_FILE=.agents/jobs/finish-pr.md AGENT_REPO="$repo" AGENT_MODEL=${AGENT_MODEL:-sonnet} \
    ISSUE_URL="https://github.com/$repo/issues/$number" \
    PR_URL="https://github.com/$repo/pull/$pr" bash "$tools/agent-run.sh" || rc=$?
done
exit $rc
