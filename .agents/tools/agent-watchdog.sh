#!/usr/bin/env bash
# Orchestrator watchdog: run in the background next to the agents you start. It exits, which wakes the orchestrator,
# when an agent's transcript has not changed for <minutes> and the agent has not finished its turn.
# Usage: agent-watchdog.sh <transcript-dir> <minutes> <agent-id>...   (transcripts are <dir>/<id>.output)
# Output: "STALE <id> <n>min;" for each quiet agent. Agents whose transcript stays empty can't be watched this way;
# they are listed as UNWATCHABLE at the start, so check their commits, PRs or test-server activity instead.
dir=$1; stale=$2; shift 2
for id in "$@"; do [ -s "$dir/$id.output" ] || echo "UNWATCHABLE $id"; done
while sleep 120; do
  now=$(date +%s); out=""
  for id in "$@"; do
    f="$dir/$id.output"; [ -s "$f" ] || continue
    age=$(( (now - $(stat -c %Y "$f")) / 60 ))
    [ "$age" -ge "$stale" ] || continue
    # A finished agent ends on a final assistant turn; its completion is reported separately.
    tail -n 1 "$f" | grep -q '"stop_reason":"end_turn"' && continue
    out="$out STALE $id ${age}min;"
  done
  [ -n "$out" ] && { echo "$out"; exit 0; }
done
