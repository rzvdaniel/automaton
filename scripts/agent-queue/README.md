# agent-queue

A task queue and orchestrator for Claude agent machines (parent: #1019 Autopilot, issue #1144).

- `agent-queue add|status|cancel|requeue|stalled|pause|resume|worker`: one worker, held by `flock`. It is the only
  process that starts `claude-task`; nothing waits on `pgrep`.
- `claude-task <name>`: runs one Claude session from a prompt on stdin (model, effort, config dir via env).
- `orchestrator.sh`: one pass, meant every 20-30 min. Honors the pause file (no task or token cap; at the usage limit the worker waits for the reset), requeues
  stalled tasks once with a note, then fails them. `ORCH_REPORT=1` also queues a morning-report task.
- `install.sh [dir]`: copies the scripts into `~/bin` (backs up same-named files). Starts nothing.
- `test.sh`: offline test with a fake runner.

State lives in `~/agent-queue` (`AGENT_QUEUE_HOME`): `pending/ running/ done/ failed/ cancelled/ logs/ PAUSE worker.log`.
Limits: no production key or production (prompts and skills), pause file.

## Install and operate (runner-01)

`bash scripts/agent-queue/install.sh --cron` copies the three scripts to `~/bin` (saving a changed file as
`<name>.bak-YYYYMMDD`) and sets the crontab: worker at `@reboot`, orchestrator every 30 min, the 06:00 morning
report (`ORCH_REPORT=1`, writes `~/agent-queue/orchestrator/morning-report.md`) and the 03:00 lessons task.
Re-running is safe. Change the repo copy, then run install again; do not edit `~/bin` by hand.
Install does not restart the worker; a running task keeps its old script until the worker is restarted.
`add` prefixes every prompt with the rules for implement (or verify, for names starting `verify`) tasks.

## Disk cleanup

`agent-cleanup worktrees` (daily 04:30) removes a git worktree and its branch only when its PR is merged or closed, the
tree is clean (no uncommitted or untracked files) and HEAD is exactly the head of the merged/closed PR (so nothing is unpushed or newer), and no running process has it as
its working directory. Ignored files (such as a local `.env`) go with the worktree. Everything else is kept and the reason is printed. `agent-cleanup caches` (Sundays 05:00)
clears NuGet and npm caches when free space is under 15 GiB. `agent-cleanup disk` prints the free space; the morning
report includes it. Repos: `AGENT_CLEAN_REPOS`; `AGENT_CLEAN_DRY=1` only prints. Not the server's own cleanup (Server Keeper).

## Pick on finish, timeout, restart (#1556)

With `AGENT_BOARD=<owner>/<project>` (for example `rzvdaniel/17`) in the worker's environment, the worker uses runner's own `gh`
login: after every task, with nothing pending, it takes the top **Ready for agents** card, queues it as `pick-<repo>-<n>`
and moves it to **Running**. A card with `needs-shape` or no **Done when** goes back to **Inbox** with one comment. A card
whose last `pick-` task failed is skipped until the issue has a newer comment. A failed or timed-out `pick-` task moves its
card to **Inbox** and comments the last 12 log lines. `agent-queue add pick-…` refuses a name that is pending, running or finished
within the hour (Overseer's Pick-up uses the same names and stays as the backstop). `AGENT_AUTOPICK=0` turns it off.
`claude-task` stops a run after `AGENT_TASK_TIMEOUT` (default `3h`, `timeout` syntax) and exits 124. To restart the worker after
an error use `agent-queue-worker.service` (systemd, `Restart=always`).
