# agent-queue

A task queue and orchestrator for Claude agent machines (parent: #1019 Autopilot, issue #1144).

- `agent-queue add|status|cancel|requeue|stalled|pause|resume|worker`: one worker, held by `flock`. It is the only
  process that starts `claude-task`; nothing waits on `pgrep`.
- `claude-task <name>`: runs one Claude session from a prompt on stdin (model, effort, config dir via env).
- `orchestrator.sh`: one pass, meant every 20-30 min. Honors the pause file and a per-day task budget, requeues
  stalled tasks once with a note, then fails them. `ORCH_REPORT=1` also queues a morning-report task.
- `install.sh [dir]`: copies the scripts into `~/bin` (backs up same-named files). Starts nothing.
- `test.sh`: offline test with a fake runner.

State lives in `~/agent-queue` (`AGENT_QUEUE_HOME`): `pending/ running/ done/ failed/ cancelled/ logs/ PAUSE worker.log`.
Limits: no production key or production (prompts and skills), pause file, `ORCH_NIGHT_BUDGET` tasks per day.
