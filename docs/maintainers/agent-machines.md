# Agent machines (GitHub Actions)

Agent work runs as GitHub Actions jobs on our own machines (#1559). One job runs at a time (`concurrency: agent`), with a
timeout and logs in the Actions run. Replaces `scripts/agent-queue/` once the pilot week passes.

| Workflow | Trigger | What it does |
| --- | --- | --- |
| `agent-issue.yml` | issue labelled `agent:go` by `rzvdaniel` or `rzvdaniel-bot` | Runs `.agents/jobs/implement-issue.md` for the issue (180 min). Success: card to **In review**. Failure or timeout: `agent:failed`, a comment with the run link, card back to **Ready for agents**. Then runs `pick`. |
| `agent-pick-next.yml` | every 10 minutes, `workflow_dispatch` | `agent-board.sh pick`: restarts the chain after a pause. |
| `agent-lessons.yml` | 03:00 daily, `workflow_dispatch` | Calls the reusable `agent-job.yml` with `.agents/jobs/lessons.md`. |
| `agent-job.yml` | `workflow_call` | Runs any prompt file; refuses actors other than the owner and bot (schedules are allowed). |

`pick` (board 17): does nothing while an `agent:go` issue is open; takes the top **Ready for agents** card; a card with
`needs-shape` or no **Done when** goes to **Inbox** with one comment; a card labelled `agent:failed` is skipped until the
issue has a newer comment; otherwise it adds `agent:go` and moves the card to **Running**.

Agent workflows never use `pull_request` triggers, and fork PRs must not be allowed to run on self-hosted runners
(Settings → Actions → General → "Require approval for all outside collaborators"; do not enable self-hosted runners for forks).
The run step calls the `claude` CLI with `CLAUDE_CODE_OAUTH_TOKEN` (not `claude-code-action`, which needs the Claude GitHub app);
the machine's `gh` login is rzvdaniel-bot. No production key is on the machine.

Before registering a runner, the owner checks (nothing in the repo enforces it): Settings → Actions → General →
"Require approval for all outside collaborators"; the runner sits in a runner group limited to the agent workflows
(`agent-issue.yml`, `agent-pick-next.yml`, `agent-lessons.yml`); no other workflow uses `runs-on: self-hosted`.
The agent runs with permissions skipped as the bot: the prompt treats text from other logins as untrusted, and only the owner's
comments reopen a failed card. `pick` serves `rzvdaniel/automaton`, `automaton-apps` and `automaton-monster` (others dispatch `agent-issue.yml` here; each needs a `~/<repo>` clone and `agent:go` and `agent:failed` labels).

## Add an agent machine

1. Install Claude Code, `gh` (logged in as rzvdaniel-bot), .NET and Node for the user `agent`; clone the repo to `~/automaton`.
2. Settings → Actions → Runners → New self-hosted runner for `rzvdaniel/automaton` (and `automaton-apps`). Follow the download steps as user `agent`,
   with `./config.sh --url <repo URL> --token <token> --labels self-hosted,agent`.
3. Install it as a service: `sudo ./svc.sh install agent && sudo ./svc.sh start`.
4. Add the repository secret `CLAUDE_CODE_OAUTH_TOKEN` (from `claude setup-token`; set it, never print it).
5. Create the labels `agent:go` and `agent:failed`. Check with `gh workflow run agent-lessons.yml`.
6. Only one machine should carry the `agent` label unless you want jobs to spread; `concurrency: agent` queues per repository, not per machine.
