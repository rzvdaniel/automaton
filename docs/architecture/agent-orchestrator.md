# Agent Orchestrator architecture

The Agent Orchestrator is a small execution supervisor for repository work.
It discovers eligible task-provider items, assigns a single fenced Git worktree,
launches Codex, retries bounded failures, reconciles after restart, and exposes
bounded operator status.

The coding agent owns engineering work: editing, testing, committing, pushing,
creating/updating a draft GitHub PR, inspecting checks and feedback, and
returning its terminal contract. GitHub is an adapter and external source of
truth; the issue remains the work item and the PR is an artifact.

The optional Reviewer is a separate generic Codex execution on the same
workspace context. It is read-only for both workspace and GitHub capability and
returns `APPROVED`, `CHANGES_REQUIRED`, or `BLOCKED`. Remediation is bounded by
`review.max_rounds`. A final approval verifies the reviewed SHA against local
and GitHub PR heads, moves the same draft PR to ready-for-review, and ends the
run. Human approval and merge are outside the orchestrator.

Durable workspace state protects the only safety-critical invariant: at most one
active execution owns a workspace. Ownership includes a launch nonce and
process identity; ambiguous recovery quarantines rather than guessing. Scheduler
attempt/backoff state decides whether eligible work may retry after ownership is
safe.

The orchestrator intentionally has no Manager role, publication state machine,
CI state machine, PR-feedback state machine, auto-approval, auto-merge, or
generic workflow/DAG engine.
