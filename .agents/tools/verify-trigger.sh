#!/usr/bin/env bash
# Decide whether a release verification run must start (issue #1800). Runs on the test server's agent runner.
#   verify-trigger.sh check   prints "run <version>" when the installed test-server version has not been verified yet
#                             and the board has issues in Verify; otherwise "skip: <reason>". Always exits 0.
#   verify-trigger.sh done [v] records version v (default: the installed one) as verified (called when a run starts, so a failed run is not retried every poll).
#                             Exits 3 without changing anything when v is already recorded: runs triggered together all
#                             passed "check" before any recorded it, so only the first one may go on.
#   verify-trigger.sh reset   forgets the recorded version, so the next poll (or a manual dispatch) verifies it again
# Env: AGENT_GH (gh), AGENT_BOARD (default rzvdaniel/17), VERIFY_VERSION_FILE (/opt/automaton-server/version),
#      VERIFY_STATE_FILE (~/.local/state/automaton/verified-version).
set -euo pipefail
GH=${AGENT_GH:-gh}
BOARD=${AGENT_BOARD:-rzvdaniel/17}
VF=${VERIFY_VERSION_FILE:-/opt/automaton-server/version}
SF=${VERIFY_STATE_FILE:-$HOME/.local/state/automaton/verified-version}
installed=$( (tr -d "[:space:]" < "$VF") 2>/dev/null || true)
case ${1:-} in
  check)
    [[ -n $installed ]] || { echo "skip: no installed version in $VF"; exit 0; }
    [[ $installed != "$(cat "$SF" 2>/dev/null || true)" ]] || { echo "skip: $installed already verified"; exit 0; }
    n=$("$GH" project item-list "${BOARD##*/}" --owner "${BOARD%%/*}" --format json -L 200 \
      --jq '[.items[]|select(.content.type=="Issue" and .status=="Verify")]|length')
    [[ ${n:-0} -gt 0 ]] || { echo "skip: nothing in Verify"; exit 0; }
    echo "run $installed" ;;
  done)
    installed=${2:-$installed}
    [[ -n $installed ]] || { echo "no installed version" >&2; exit 1; }
    mkdir -p "$(dirname "$SF")"
    # The lock makes the test and the write one step, so two runs cannot both claim the version.
    exec 9> "$SF.lock"; flock 9
    [[ $installed != "$(cat "$SF" 2>/dev/null || true)" ]] || { echo "skip: $installed already claimed"; exit 3; }
    echo "$installed" > "$SF" ;;
  reset) rm -f "$SF" ;;
  *) echo "usage: verify-trigger.sh check|done|reset" >&2; exit 2 ;;
esac
