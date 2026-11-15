#!/usr/bin/env bash
# Install agent-queue into a bin directory (default ~/bin; override with the first argument).
# It only copies scripts; it does not start the worker or the orchestrator, and it
# leaves existing files named differently (queue.sh, claude-task-haiku) alone.
set -euo pipefail
dest=${1:-$HOME/bin}; src=$(dirname "$(readlink -f "$0")")
mkdir -p "$dest" "${AGENT_QUEUE_HOME:-$HOME/agent-queue}"
for f in agent-queue claude-task orchestrator.sh; do
  [[ -e $dest/$f && ! $dest/$f -ef $src/$f ]] && cp -p "$dest/$f" "$dest/$f.bak"
  install -m 0755 "$src/$f" "$dest/$f"
done
echo "installed to $dest. Start: nohup $dest/agent-queue worker &  (or run it from systemd); orchestrator: $dest/orchestrator.sh every 20-30 min."
