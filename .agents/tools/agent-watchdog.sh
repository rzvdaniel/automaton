#!/usr/bin/env bash
# Orchestrator watchdog: run in the background next to the agents you start. It exits, which wakes the orchestrator,
# when an agent's transcript has not changed for <minutes> and the agent has not finished its turn.
# Usage: agent-watchdog.sh <transcript-dir> <minutes> <agent-id>...   (transcripts are <dir>/<id>.output)
# Output: "STALE <id> <n>min;" for each quiet agent. Agents whose transcript stays empty can't be watched this way;
# they are listed as UNWATCHABLE, so check their commits, PRs or test-server activity instead.
[ $# -ge 3 ] || { echo "usage: $0 <transcript-dir> <minutes> <agent-id>..."; exit 2; }
dir=$1; stale=$2; shift 2
mtime() { stat -c %Y "$1" 2>/dev/null || stat -f %m "$1"; }  # GNU, then BSD/macOS
watchable=0
for id in "$@"; do
  if [ -s "$dir/$id.output" ]; then watchable=1; else echo "UNWATCHABLE $id"; fi
done
[ "$watchable" = 1 ] || { echo "NOTHING TO WATCH"; exit 0; }
while sleep 120; do
  now=$(date +%s); out=""
  for id in "$@"; do
    f="$dir/$id.output"; [ -s "$f" ] || continue
    age=$(( (now - $(mtime "$f")) / 60 ))
    [ "$age" -ge "$stale" ] || continue
    # A finished agent ends on a final assistant turn; its completion is reported separately.
    tail -n 1 "$f" | grep -Eq '"stop_reason" *: *"end_turn"' && continue
    out="$out STALE $id ${age}min;"
  done
  [ -n "$out" ] && { echo "$out"; exit 0; }
done
