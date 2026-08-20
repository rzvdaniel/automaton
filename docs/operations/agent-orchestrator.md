# Agent Orchestrator operator guide

The orchestrator discovers eligible GitHub issues, assigns one isolated worktree,
and supervises Codex. The Developer edits, tests, commits, pushes, and creates a
draft PR. An optional independent Reviewer is read-only. After approval, the
orchestrator verifies the reviewed local and PR heads match, marks the same PR
ready for review, and stops. Humans approve and merge.

## Trusted workflow configuration

`WORKFLOW.md` is trusted host configuration, not issue content. Keep it outside
the repository checkout and workspace root. An issue is eligible only when it is
open and has every configured `tracker.required_labels` label.

Production defaults are Developer `gpt-5.6-terra`, Reviewer `gpt-5.5`, one
concurrent agent, two review rounds, a five-minute stall timeout, and a
60-minute emergency turn ceiling. The Developer is read-write/GitHub-write; the
Reviewer is read-only/GitHub-read-only. Unsafe role profiles fail startup.

Credentials are supplied through explicit environment references in the
workflow. Never commit tokens, prompts, or Codex authentication state.

## State, recovery, and investigation

Operator files are daemon-owned under:

```text
<workspace-root>/.automaton-agent-orchestrator/operator/
  status.json
  events.jsonl
  events.jsonl.1
```

`status.json` identifies the running issue, workspace, branch, ownership,
attempt, terminal state, build revision, and reconciliation health.
`events.jsonl` contains bounded lifecycle events including workspace claim,
Developer/Reviewer model and round, remediation, handoff, completion, and
shutdown. It never contains tokens, prompts, transcripts, command output, or
raw provider payloads.

On restart, ownership is resolved first. A proven-dead execution is released and
may retry within its budget; ambiguous ownership is quarantined for operator
attention. Do not edit sidecars or operator files to force progress.

## Service lifecycle

Run one daemon per workspace root under a host service manager. Linux production
uses systemd; stop with SIGTERM and let the daemon release or quarantine active
ownership before the service manager's bounded final kill. Logs and workspace
sidecars survive restart. Verify the reported build revision before dispatch.

## Non-goals

The orchestrator has no Manager role, CI state machine, PR-feedback state
machine, auto-approval, auto-merge, or merge automation. GitHub remains the
source of truth for PR state; human review and merge stay external.
