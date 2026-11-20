#!/usr/bin/env bash
# Install agent-queue into a bin directory (default ~/bin; first argument overrides).
#   install.sh [dir] [--cron]
# Copies agent-queue, claude-task and orchestrator.sh (an existing different file is first saved as
# <name>.bak-YYYYMMDD). Files are replaced by a new inode, so a running worker is not disturbed.
# It does not start or stop the worker. With --cron it also sets the crontab entries below
# (idempotent: lines marked "# agent-queue" are replaced, other lines are kept).
set -euo pipefail
dest=$HOME/bin; cron=0
for a in "$@"; do [[ $a == --cron ]] && cron=1 || dest=$a; done
src=$(dirname "$(readlink -f "$0")")
mkdir -p "$dest" "${AGENT_QUEUE_HOME:-$HOME/agent-queue}" "$HOME/claude-tasks"
for f in agent-queue claude-task orchestrator.sh; do
  if [[ -e $dest/$f ]] && ! cmp -s "$dest/$f" "$src/$f"; then cp -p "$dest/$f" "$dest/$f.bak-$(date +%Y%m%d)"; fi
  install -m 0755 "$src/$f" "$dest/$f"
done
echo "installed to $dest"
if (( cron )); then
  env_="env PATH=$dest:$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin AGENT_QUEUE_RUNNER=$dest/claude-task"
  orch="$env_ ORCH_STALL_MINUTES=120"
  out=">> $HOME/claude-tasks"
  new=$(cat <<C
@reboot $env_ nohup $dest/agent-queue worker $out/worker.out 2>&1 & # agent-queue
*/30 * * * * $orch $dest/orchestrator.sh $out/orchestrator.out 2>&1 # agent-queue
0 6 * * * $orch ORCH_REPORT=1 $dest/orchestrator.sh $out/orchestrator.out 2>&1 # agent-queue
0 3 * * * $env_ /bin/sh -c 'echo "Follow .agents/skills/automaton-lessons-learned/SKILL.md end to end on the lessons inbox (the open issue labelled lessons). Reply on the inbox." | $dest/agent-queue add lessons-\$(date +\%Y\%m\%d)' $out/lessons-cron.out 2>&1 # agent-queue
C
)
  { crontab -l 2>/dev/null | grep -v '# agent-queue$' | grep -vE 'agent-queue worker|orchestrator\.sh|agent-queue add lessons' || true; echo "$new"; } | crontab -
  echo "crontab updated (worker is not restarted; start it with: nohup $dest/agent-queue worker &)"
fi
