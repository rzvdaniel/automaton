#!/usr/bin/env bash
# File or comment on one lesson issue (and place it on board 17 Inbox) when the nightly release install check fails.
# Environment: REPO, COMMIT_SHA, RUN_URL, FAILED_JOBS (optional), ISSUE_TITLE (optional),
# AGENT_BOARD (default rzvdaniel/17), BOARD_SCRIPT (default .agents/tools/agent-board.sh),
# AGENT_GH_TOKEN (optional, needed for board 17), GITHUB_TOKEN (fallback for repo issues).
set -euo pipefail

GH=${GH_BIN:-gh}
REPO=${REPO:?REPO is required}
COMMIT_SHA=${COMMIT_SHA:?COMMIT_SHA is required}
RUN_URL=${RUN_URL:?RUN_URL is required}
FAILED_JOBS=${FAILED_JOBS:-unknown}
ISSUE_TITLE=${ISSUE_TITLE:-Nightly release install check on main failed}
BOARD=${AGENT_BOARD:-rzvdaniel/17}
BOARD_SCRIPT=${BOARD_SCRIPT:-.agents/tools/agent-board.sh}
BOARD_OWNER=${BOARD%%/*}
BOARD_NUM=${BOARD##*/}

ISSUE_TOKEN=${AGENT_GH_TOKEN:-${GITHUB_TOKEN:-${GH_TOKEN:-}}}
BOARD_TOKEN=${AGENT_GH_TOKEN:-${GH_TOKEN:-}}

run_gh_issue() {
    if [[ -n "$ISSUE_TOKEN" ]]; then
        if GH_TOKEN="$ISSUE_TOKEN" "$GH" "$@"; then
            return 0
        fi
        if [[ -n "${GITHUB_TOKEN:-}" && "$ISSUE_TOKEN" != "$GITHUB_TOKEN" ]]; then
            GH_TOKEN="$GITHUB_TOKEN" "$GH" "$@"
            return $?
        fi
        return 1
    fi
    "$GH" "$@"
}

ensure_board_inbox() {
    local issue_num=$1 issue_url=$2
    if [[ -z "$BOARD_TOKEN" ]]; then
        echo "::warning::AGENT_GH_TOKEN is not set; move #$issue_num to Inbox on board $BOARD." >&2
        return 0
    fi
    if ! GH_TOKEN="$BOARD_TOKEN" "$GH" project item-add "$BOARD_NUM" --owner "$BOARD_OWNER" --url "$issue_url" >/dev/null 2>&1; then
        echo "::warning::Could not add #$issue_num to board $BOARD; move it to Inbox by hand." >&2
        return 0
    fi
    local attempt moved=false
    for attempt in 1 2 3; do
        if AGENT_GH="$GH" AGENT_BOARD="$BOARD" GH_TOKEN="$BOARD_TOKEN" bash "$BOARD_SCRIPT" move "$REPO" "$issue_num" Inbox 2>/dev/null; then
            moved=true
            break
        fi
        sleep 2
    done
    if ! $moved; then
        echo "::warning::Added #$issue_num to board $BOARD, but could not move it to Inbox." >&2
    fi
}

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

existing_json=$(run_gh_issue issue list --repo "$REPO" --state open --limit 100 --json number,title,url,labels)
existing_row=$(EXISTING_JSON="$existing_json" python3 - "$ISSUE_TITLE" <<'PY'
import json, os, sys
target = sys.argv[1]
for item in json.loads(os.environ["EXISTING_JSON"]):
    if item.get("title") == target:
        labels = ",".join(lbl.get("name", "") for lbl in item.get("labels", []))
        print(f"{item['number']}\t{item['url']}\t{labels}")
        break
PY
)

if [[ -n "$existing_row" ]]; then
    IFS=$'\t' read -r issue_num issue_url issue_labels <<< "$existing_row"
    if [[ ",$issue_labels," != *",lesson,"* ]]; then
        run_gh_issue issue edit "$issue_num" --repo "$REPO" --add-label lesson >/dev/null || true
    fi
    cat > "$work/comment.md" <<EOF
Nightly release install check (\`mode=all\`) failed again on \`main\`:

- **Commit:** \`$COMMIT_SHA\`
- **Failed job(s):** $FAILED_JOBS
- **Run:** $RUN_URL
EOF
    run_gh_issue issue comment "$issue_num" --repo "$REPO" --body-file "$work/comment.md" >/dev/null
    if [[ -n "$BOARD_TOKEN" ]]; then
        on_board=$(GH_TOKEN="$BOARD_TOKEN" "$GH" project item-list "$BOARD_NUM" --owner "$BOARD_OWNER" --format json -L 200 \
            --jq "[.items[] | select(.content.type == \"Issue\" and .content.repository == \"$REPO\" and .content.number == $issue_num)] | length" 2>/dev/null || echo 1)
        if [[ "$on_board" == 0 ]]; then
            ensure_board_inbox "$issue_num" "$issue_url"
        fi
    fi
    echo "Commented on existing lesson issue #$issue_num ($issue_url)"
    exit 0
fi

short_sha=${COMMIT_SHA:0:12}
cat > "$work/body.md" <<EOF
**Problem**
The nightly release install check (\`mode=all\`) failed on \`main\` at commit \`$short_sha\`.

**Steps**
1. \`Release install check\` built images and the server bundle from \`main\` (\`$COMMIT_SHA\`).
2. \`scripts/ci/release-install-check.sh all\` ran upgrade, fresh install, external/API scans, and backup/restore on a throwaway Hetzner server.

**Result**
Failed in job(s): \`$FAILED_JOBS\`.
Run: $RUN_URL

**Expected**
The nightly \`Release install check\` (\`mode=all\`) passes on \`main\`, so releases can be cut without regressions.

**Done when**
The root cause is fixed and \`Release install check\` (\`mode=all\`) passes on \`main\`.
EOF

issue_url=$(run_gh_issue issue create --repo "$REPO" --label lesson --title "$ISSUE_TITLE" --body-file "$work/body.md")
issue_num=${issue_url##*/}
ensure_board_inbox "$issue_num" "$issue_url"
echo "Filed lesson issue #$issue_num ($issue_url)"
