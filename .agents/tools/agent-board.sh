#!/usr/bin/env bash
# Board helper for agent workflows (board AGENT_BOARD, default rzvdaniel/17). Uses the runner's `gh` login.
#   agent-board.sh move <owner/repo> <number> <Status>   move the card
#   agent-board.sh pick                                  label the top shaped Ready card agent:go and move it to Running
# pick skips cards whose last run failed until the owner comments again (label agent:failed, removed by a newer comment),
# sends needs-shape or no-"Done when" cards back to Inbox with one comment, and does nothing while an agent:go issue is open.
set -euo pipefail
GH=${AGENT_GH:-gh}
BOARD=${AGENT_BOARD:-rzvdaniel/17}
# Repositories whose Ready cards are picked. The runner and agent-issue.yml live in automaton; other repos are started
# with a workflow_dispatch of agent-issue.yml there.
REPOS=${AGENT_REPOS:-automaton automaton-apps}
OWNER=${BOARD%%/*}; NUM=${BOARD##*/}

items() { "$GH" project item-list "$NUM" --owner "$OWNER" --format json -L 200 \
  --jq '.items[]|select(.content.type=="Issue")|[.id,.content.repository,.content.number,.status]|@tsv'; }

move() { # repo number status
  local id pid f
  id=$(items | awk -F'\t' -v r="$1" -v n="$2" '$2==r && $3==n {print $1; exit}'); [[ -n $id ]] || { echo "card $1#$2 not on board" >&2; return 1; }
  pid=$("$GH" project view "$NUM" --owner "$OWNER" --format json --jq .id)
  f=$("$GH" project field-list "$NUM" --owner "$OWNER" --format json \
    --jq ".fields[]|select(.name==\"Status\")|[.id,(.options[]|select(.name==\"$3\")|.id)]|@tsv")
  [[ $f == *$'\t'* ]] || { echo "no status $3" >&2; return 1; }
  "$GH" project item-edit --id "$id" --project-id "$pid" --field-id "${f%%$'\t'*}" --single-select-option-id "${f#*$'\t'}" >/dev/null
}

pick() {
  local id repo num st info nsl done_ failed last
  local r; for r in $REPOS; do repo="$OWNER/$r"
    [[ -n $("$GH" issue list -R "$repo" -l agent:go -s open --json number --jq '.[].number') ]] && { echo "an agent:go issue is open in $repo; nothing to pick"; return 0; }
  done
  while IFS=$'\t' read -r id repo num st; do
    [[ $st == "Ready for agents" && " $REPOS " == *" ${repo#*/} "* ]] || continue
    info=$("$GH" issue view "$num" -R "$repo" --json labels,body,comments --jq \
      '[([.labels[].name]|index("needs-shape")!=null), (.body|test("(^|\\n)\\W*Done when";"i")), ([.labels[].name]|index("agent:failed")!=null), ((.comments|map(select(.author.login=="'"$OWNER"'"))|map(.createdAt)|last) // "none")]|@tsv') || continue
    IFS=$'\t' read -r nsl done_ failed last <<< "$info"
    if [[ $nsl == true || $done_ != true ]]; then
      "$GH" issue comment "$num" -R "$repo" --body "Moved back to Inbox: the issue has the needs-shape label or no Done when section. Add a Done when, then move the card to Ready for agents." >/dev/null || true
      move "$repo" "$num" Inbox || true; continue
    fi
    if [[ $failed == true ]]; then
      # the failure comment is the last one the workflow posted; a newer comment from the owner reopens the card
      local flast; flast=$("$GH" issue view "$num" -R "$repo" --json comments --jq '[.comments[]|select(.body|startswith("Agent run failed"))|.createdAt]|last // "none"')
      if [[ $flast != none && $last != none && $last > $flast ]]; then "$GH" issue edit "$num" -R "$repo" --remove-label agent:failed >/dev/null || true; else continue; fi
    fi
    # agent:go starts agent-issue.yml only in automaton. Elsewhere dispatch the run first (the runner's login is the
    # allowlisted bot); only then mark the issue taken, so a failed dispatch never leaves agent:go blocking the queue.
    if [[ $repo != "$OWNER/automaton" ]]; then
      "$GH" workflow run agent-issue.yml -R "$OWNER/automaton" -f repo="$repo" -f number="$num" >/dev/null         || { echo "could not start $repo#$num" >&2; return 1; }
    fi
    "$GH" issue edit "$num" -R "$repo" --add-label agent:go >/dev/null
    move "$repo" "$num" Running || true
    echo "picked $repo#$num"; return 0
  done < <(items)
  echo "no Ready card"
}

case ${1:-} in
  move) move "${2:?repo}" "${3:?number}" "${4:?status}" ;;
  pick) pick ;;
  *) echo "usage: agent-board.sh move <owner/repo> <number> <Status> | pick" >&2; exit 2 ;;
esac
