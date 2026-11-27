# Agent machines (GitHub Actions)

Agent work runs as GitHub Actions jobs on our own machines (#1559). One job runs at a time (`concurrency: agent`), with a
timeout and logs in the Actions run. Replaces `scripts/agent-queue/` once the pilot week passes.

| Workflow | Trigger | What it does |
| --- | --- | --- |
| `agent-issue.yml` | issue labelled `agent:go` by `rzvdaniel` or `rzvdaniel-bot` | Runs `.agents/jobs/implement-issue.md` for the issue (180 min). Then, if an open PR of the issue lacks only the main review and/or security pass, `agent-finish-pr.sh` runs `.agents/jobs/finish-pr.md` (60 min): reviewers post with the runner's other logins and the gate merges. Success: card to **Verify**. Failure or timeout: `agent:failed`, a comment with the run link, card to **Inbox** (it needs the owner; Ready only holds work `pick` takes). Agent Keeper's **Pick up** Action starts the next card within 10 minutes. |

Picking (Agent Keeper's Pick up, board 17; `agent-board.sh pick` by hand): does nothing while an `agent:go` issue is open; takes the top **Ready for agents** card; a card with
`needs-shape` or no **Done when** goes to **Inbox** with one comment; a card labelled `agent:failed` is skipped until the
issue has a newer comment; otherwise it adds `agent:go` and moves the card to **Running**.

Agent workflows never use `pull_request` triggers, and fork PRs must not be allowed to run on self-hosted runners
(Settings → Actions → General → "Require approval for all outside collaborators"; do not enable self-hosted runners for forks).
The run step calls the `claude` CLI with `CLAUDE_CODE_OAUTH_TOKEN` (not `claude-code-action`, which needs the Claude GitHub app);
the machine's `gh` login is rzvdaniel-bot. No production key is on the machine.

Before registering a runner, the owner checks (nothing in the repo enforces it): Settings → Actions → General →
"Require approval for all outside collaborators"; the runner sits in a runner group limited to the agent workflows
(`agent-issue.yml`); no other workflow uses `runs-on: self-hosted`.
The agent runs with permissions skipped as the bot: the prompt treats text from other logins as untrusted, and only the owner's
comments reopen a failed card. `pick` serves `rzvdaniel/automaton`, `automaton-apps` and `automaton-monster` (others dispatch `agent-issue.yml` here; each needs a `~/<repo>` clone and `agent:go` and `agent:failed` labels).

## Add an agent machine

1. Install Claude Code, `gh` (logged in as rzvdaniel-bot), .NET and Node for the user `agent`; clone the repo to `~/automaton`.
2. Settings → Actions → Runners → New self-hosted runner for `rzvdaniel/automaton` (and `automaton-apps`). Follow the download steps as user `agent`,
   with `./config.sh --url <repo URL> --token <token> --labels self-hosted,agent`.
3. Install it as a service: `sudo ./svc.sh install agent && sudo ./svc.sh start`.
4. Add the repository secret `CLAUDE_CODE_OAUTH_TOKEN` (from `claude setup-token`; set it, never print it).
5. Create the labels `agent:go` and `agent:failed`. Install Agent Keeper (automaton-apps) on the machine; its Pick up Action starts the queue.
6. Only one machine should carry the `agent` label unless you want jobs to spread; `concurrency: agent` queues per repository, not per machine.

## Which agent runs (#1864)

`agent-run.sh` runs Claude Code (`claude`) or Antigravity (`agy`), set by the repo variables `AGENT_CLI` (`claude` default, or `agy`) and `AGENT_MODEL` (default `sonnet`, or `gemini-4-hydrogen-high` with agy). With agy, a run that fails (a usage limit included) or leaves no pull request for the issue runs once more on Claude from a clean worktree. Each implementer run posts one line on the issue: tool, model, minutes, fallback. Reviews and the security pass always run on Claude (`AGENT_REVIEW_MODEL`, default `sonnet`), because they start `automaton-reviewer` subagents. agy needs a one-time sign-in as the machine user (`~/.local/bin/agy`).
