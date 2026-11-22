#!/usr/bin/env bash
# One orchestrator pass (run it from cron or a loop every 20-30 minutes):
#   1. pause file -> do nothing
#   2. stalled running tasks: requeue with a note (up to ORCH_MAX_RETRIES), then fail them
#   3. optionally (ORCH_REPORT=1) start a Claude session that writes the morning report
# Never uses the production key, never merges; those limits live in the task prompts and skills.
set -euo pipefail
Q=${AGENT_QUEUE_HOME:-$HOME/agent-queue}
AQ=${AGENT_QUEUE_BIN:-$(dirname "$(readlink -f "$0")")/agent-queue}
STALL_MIN=${ORCH_STALL_MINUTES:-30}
MAX_RETRIES=${ORCH_MAX_RETRIES:-1}
mkdir -p "$Q/orchestrator"; log() { echo "$(date -Is) $*" | tee -a "$Q/orchestrator/orchestrator.log"; }
[[ -e $Q/PAUSE ]] && { log "paused, nothing to do"; exit 0; }
"$AQ" stalled "$STALL_MIN" | while read -r name idle; do
  tries=$(grep -c " retry $name\$" "$Q/orchestrator/orchestrator.log" 2>/dev/null || true)
  if (( ${tries:-0} < MAX_RETRIES )); then
    log "stalled $name ($idle idle), retry $name"
    "$AQ" requeue "$name" "The previous run stalled after $idle without output. Continue from the current git and GitHub state; do not redo finished steps." || true
  else
    log "stalled $name ($idle idle), retries used, failing it"
    "$AQ" cancel "$name" || true
  fi
done
if [[ ${ORCH_REPORT:-} == 1 ]]; then
  { "$AQ" status; "$(dirname "$AQ")/agent-cleanup" disk; } > "$Q/orchestrator/status.txt"
  printf 'Read %s and the GitHub state of the issues named there. Write a short morning report to %s: merged PRs, blocked tasks, latest test-server release, and the "free disk" line from the file (say so if it is under 15 GiB). Do not merge, release or touch production.\n' \
    "$Q/orchestrator/status.txt" "$Q/orchestrator/morning-report.md" | "$AQ" add "report-$(date +%Y%m%d%H%M)" || true
fi
