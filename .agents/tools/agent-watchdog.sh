#!/usr/bin/env bash
# Orchestrator watchdog: run in the background next to the agents you start. It exits, which wakes the orchestrator,
# when an agent's transcript has not changed for <minutes> and the agent has not finished its turn.
# Usage: agent-watchdog.sh <transcript-dir> <minutes> <agent-id>...   (transcripts are <dir>/<id>.output)
# Output: "STALE <id> <n>min;" for each quiet agent. A transcript is often still empty right after the agent starts,
# so an empty one is watched until it fills; one that stays empty for <minutes> is reported as "UNWATCHABLE <id>;",
# so check that agent's commits, PRs or test-server activity instead.
[ $# -ge 3 ] || { echo "usage: $0 <transcript-dir> <minutes> <agent-id>..."; exit 2; }
dir=$1; stale=$2; shift 2
interval=${WATCHDOG_INTERVAL:-120}
mtime() { stat -c %Y "$1" 2>/dev/null || stat -f %m "$1"; }  # GNU, then BSD/macOS
started=$(date +%s)
while sleep "$interval"; do
  now=$(date +%s); out=""
  for id in "$@"; do
    f="$dir/$id.output"
    if [ ! -s "$f" ]; then
      [ $(( (now - started) / 60 )) -ge "$stale" ] && out="$out UNWATCHABLE $id;"
      continue
    fi
    age=$(( (now - $(mtime "$f")) / 60 ))
    [ "$age" -ge "$stale" ] || continue
    # A finished agent ends on a final assistant turn; its completion is reported separately.
    tail -n 1 "$f" | grep -Eq '"stop_reason" *: *"end_turn"' && continue
    out="$out STALE $id ${age}min;"
  done
  [ -n "$out" ] && { echo "$out"; exit 0; }
done
