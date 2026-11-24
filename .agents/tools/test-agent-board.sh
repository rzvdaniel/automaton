#!/usr/bin/env bash
# Offline test of agent-board.sh pick with a fake gh. Run: bash .agents/tools/test-agent-board.sh
set -euo pipefail
d=$(dirname "$(readlink -f "$0")"); t=$(mktemp -d); trap 'rm -rf "$t"' EXIT
cat > "$t/gh" <<'F'
#!/usr/bin/env bash
echo "$*" >> "$T/calls"
case "$1 $2" in
 "project item-list") printf 'i1\trzvdaniel/automaton\t1\tReady for agents\ni2\trzvdaniel/automaton\t2\tReady for agents\ni3\trzvdaniel/automaton\t3\tReady for agents\ni4\trzvdaniel/automaton-apps\t4\tReady for agents\ni5\trzvdaniel/automaton-monster\t5\tReady for agents\n' ;;
 "project view") echo PID ;;
 "project field-list") printf 'FID\tOPT\n' ;;
 "issue list") ;;
 "issue view") case "$3" in
    1) printf 'true\ttrue\tfalse\tnone\n' ;;
    2) if [[ "$*" == *'"Agent run failed"'* || "$*" == *startswith* ]]; then echo 2026-01-02T00:00:00Z; else printf 'false\ttrue\ttrue\t2026-01-01T00:00:00Z\n'; fi ;;
    3) printf 'false\ttrue\tfalse\tnone\n' ;; esac ;;
esac
F
chmod +x "$t/gh"; export T=$t AGENT_GH=$t/gh
out=$(bash "$d/agent-board.sh" pick)
[[ $out == "picked rzvdaniel/automaton#3" ]] || { echo "FAIL: $out"; exit 1; }
grep -q 'issue comment 1 ' "$t/calls" || { echo "FAIL: unshaped card 1 not commented"; exit 1; }
grep -q 'issue edit 3 .*--add-label agent:go' "$t/calls" || { echo "FAIL: card 3 not labelled"; exit 1; }
! grep -q 'issue edit 2 \|issue edit 4 \|issue edit 5 ' "$t/calls" || { echo "FAIL: failed, apps or monster card touched"; exit 1; }
echo "ok"
