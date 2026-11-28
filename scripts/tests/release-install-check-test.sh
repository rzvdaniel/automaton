#!/usr/bin/env bash
# Unit tests for wait_install_lock and restore_failure_message in scripts/ci/release-install-check.sh.
#   bash scripts/tests/release-install-check-test.sh
set -euo pipefail

repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
fail_test() { echo "FAIL: $*" >&2; exit 1; }

source <(sed -n '/^# restore-wait:begin/,/^# restore-wait:end/p' "$repo/scripts/ci/release-install-check.sh")

on_failure_calls=0
last_fail=''
on_failure() { on_failure_calls=$((on_failure_calls + 1)); }
fail() { last_fail="$*"; return 1; }

# 1. Real flock held briefly, then released: wait_install_lock polls and succeeds.
lock="$work/install.lock"
remote() { bash -c "${1//\/run\/lock\/automaton-install.lock/$lock}"; }
(
    exec 9>"$lock"
    flock -x 9
    sleep 0.4
) &
holder=$!
sleep 0.1
INSTALL_LOCK_WAIT_SECONDS=5 INSTALL_LOCK_POLL_SECONDS=0.1 wait_install_lock || fail_test "wait_install_lock failed after lock release: $last_fail"
wait "$holder"
[[ "$on_failure_calls" -eq 0 ]] || fail_test 'on_failure should not be called when lock releases'

# 2. Real flock held past deadline: wait_install_lock times out with lock-specific message.
(
    exec 9>"$lock"
    flock -x 9
    sleep 3
) &
holder=$!
sleep 0.1
on_failure_calls=0
last_fail=''
if INSTALL_LOCK_WAIT_SECONDS=1 INSTALL_LOCK_POLL_SECONDS=0.2 wait_install_lock; then
    kill "$holder" 2>/dev/null || true
    fail_test 'expected wait_install_lock to time out while lock was held'
fi
kill "$holder" 2>/dev/null || true
wait "$holder" 2>/dev/null || true
[[ "$on_failure_calls" -eq 1 ]] || fail_test "expected 1 on_failure call, got $on_failure_calls"
[[ "$last_fail" == *"First-boot install did not finish and release /run/lock/automaton-install.lock within 1s"* ]] \
    || fail_test "unexpected lock timeout message: $last_fail"

# 3. SSH failure past deadline: timeout message reports SSH/command exit code, not lock held.
remote() { return 255; }
on_failure_calls=0
last_fail=''
if INSTALL_LOCK_WAIT_SECONDS=1 INSTALL_LOCK_POLL_SECONDS=0.2 wait_install_lock; then
    fail_test 'expected wait_install_lock to fail on SSH error'
fi
[[ "$last_fail" == *"Could not check /run/lock/automaton-install.lock via SSH within 1s (last exit 255)"* ]] \
    || fail_test "unexpected SSH timeout message: $last_fail"

# 4. restore_failure_message names the real cause.
printf 'Another installation, update or restore is running.\n' > "$work/lock.log"
msg=$(restore_failure_message "$work/lock.log")
[[ "$msg" == *'holding /run/lock/automaton-install.lock'* && "$msg" != *key* ]] \
    || fail_test "unexpected lock failure message: $msg"

printf 'age: error: no identity matched any of the recipients\n' > "$work/key.log"
msg=$(restore_failure_message "$work/key.log")
[[ "$msg" == *'decryption failed'* && "$msg" == *key* ]] \
    || fail_test "unexpected decryption failure message: $msg"

printf 'Archive or key file not found.\n' > "$work/missing.log"
msg=$(restore_failure_message "$work/missing.log")
[[ "$msg" == *'not found'* && "$msg" != *decryption* ]] \
    || fail_test "unexpected missing-file message: $msg"

printf 'pg_restore: error: could not execute query\n' > "$work/other.log"
msg=$(restore_failure_message "$work/other.log")
[[ "$msg" == *'did not finish'* && "$msg" != *key* && "$msg" != *decryption* ]] \
    || fail_test "unexpected generic failure message: $msg"

# 5. report-install-check-failure.sh files one lesson issue on board 17 Inbox and comments without duplicates.
state="$work/gh-state"
mkdir -p "$state"
cat > "$work/fake-gh" <<'FAKEGH'
#!/usr/bin/env python3
import json, os, sys
from pathlib import Path

state = Path(os.environ["FAKE_GH_STATE"])
issues_file = state / "issues.json"
board_file = state / "board.json"
issues = json.loads(issues_file.read_text()) if issues_file.exists() else []
board = json.loads(board_file.read_text()) if board_file.exists() else {}

args = sys.argv[1:]
def flag(name):
    if name in args:
        return args[args.index(name) + 1]
    return ""

if args[:2] == ["issue", "list"]:
    st = flag("--state")
    out = [i for i in issues if st == "all" or i["state"] == st]
    print(json.dumps(out))
elif args[:2] == ["issue", "create"]:
    num = len(issues) + 100
    body = Path(flag("--body-file")).read_text()
    labels = [{"name": args[i + 1]} for i, a in enumerate(args) if a == "--label"]
    url = f"https://github.com/rzvdaniel/automaton/issues/{num}"
    issues.append({"number": num, "title": flag("--title"), "body": body, "url": url, "state": "open", "labels": labels, "comments": []})
    issues_file.write_text(json.dumps(issues))
    print(url)
elif args[:2] == ["issue", "comment"]:
    num = int(args[2])
    body = Path(flag("--body-file")).read_text()
    for i in issues:
        if i["number"] == num:
            i["comments"].append(body)
    issues_file.write_text(json.dumps(issues))
elif args[:2] == ["project", "item-add"]:
    url = flag("--url")
    board[url] = board.get(url, "None")
    board_file.write_text(json.dumps(board))
elif args[:2] == ["project", "item-list"]:
    out = {"items": [{"id": f"ITEM-{u.rsplit('/', 1)[-1]}", "content": {"type": "Issue", "repository": "rzvdaniel/automaton", "number": int(u.rsplit('/', 1)[-1])}, "status": st} for u, st in board.items()]}
    if "--jq" in args:
        # Minimal handler for the count check in report-install-check-failure.sh
        print(len(out["items"]))
    else:
        print(json.dumps(out))
else:
    raise SystemExit(f"unexpected fake gh call: {args}")
FAKEGH
chmod +x "$work/fake-gh"

cat > "$work/fake-board.sh" <<'FAKEBOARD'
#!/usr/bin/env bash
set -euo pipefail
echo "$*" >> "$FAKE_GH_STATE/board-moves.log"
FAKEBOARD
chmod +x "$work/fake-board.sh"

FAKE_GH_STATE="$state" GH_BIN="$work/fake-gh" AGENT_GH_TOKEN="tok" GITHUB_TOKEN="tok" \
    REPO="rzvdaniel/automaton" COMMIT_SHA="1111222233334444" \
    RUN_URL="https://github.com/rzvdaniel/automaton/actions/runs/5001" FAILED_JOBS="check" \
    BOARD_SCRIPT="$work/fake-board.sh" \
    bash "$repo/scripts/ci/report-install-check-failure.sh"

python3 - "$state/issues.json" "$state/board.json" "$state/board-moves.log" "$repo/.agents/tools/issue-check.py" <<'PY'
import importlib.util, json, sys
from pathlib import Path

issues = json.loads(Path(sys.argv[1]).read_text())
board = json.loads(Path(sys.argv[2]).read_text())
moves = Path(sys.argv[3]).read_text().splitlines()
assert len(issues) == 1, issues
assert issues[0]["labels"] == [{"name": "lesson"}], issues[0]["labels"]
assert "https://github.com/rzvdaniel/automaton/issues/100" in board
assert moves == ["move rzvdaniel/automaton 100 Inbox"], moves

spec = importlib.util.spec_from_file_location("issue_check", sys.argv[4])
ic = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ic)
missing = ic.missing(issues[0]["body"], ["lesson"])
assert not missing, f"issue-check found missing sections: {missing}"
PY

FAKE_GH_STATE="$state" GH_BIN="$work/fake-gh" AGENT_GH_TOKEN="tok" GITHUB_TOKEN="tok" \
    REPO="rzvdaniel/automaton" COMMIT_SHA="5555666677778888" \
    RUN_URL="https://github.com/rzvdaniel/automaton/actions/runs/5002" FAILED_JOBS="images,check" \
    BOARD_SCRIPT="$work/fake-board.sh" \
    bash "$repo/scripts/ci/report-install-check-failure.sh"

python3 - "$state/issues.json" "$state/board-moves.log" <<'PY'
import json, sys
from pathlib import Path
issues = json.loads(Path(sys.argv[1]).read_text())
moves = Path(sys.argv[2]).read_text().splitlines()
assert len(issues) == 1, f"expected no duplicate issue, got {len(issues)}"
assert len(issues[0]["comments"]) == 1, issues[0]["comments"]
assert "5555666677778888" in issues[0]["comments"][0]
assert "images,check" in issues[0]["comments"][0]
assert "https://github.com/rzvdaniel/automaton/actions/runs/5002" in issues[0]["comments"][0]
assert len(moves) == 1, "existing board item should not be moved again"
PY

echo 'release-install-check-test: ok'
