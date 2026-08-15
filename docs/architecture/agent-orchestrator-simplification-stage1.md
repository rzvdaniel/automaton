# Agent Orchestrator simplification — Stage 1 contract

Status: proposal only. This stage changes documentation, not runtime behavior.

## Objective

Move the orchestrator toward a thin Symphony-style supervisor: it discovers and
claims work, creates an isolated workspace, owns the Codex process boundary,
and records a small terminal outcome. Codex owns engineering decisions and the
repository workflow (edit, test, commit, push, PR, CI, and review response).

## Keep

- `OrchestratorHostedService`, scheduler/claim logic, eligibility, concurrency,
  retry/backoff, cancellation, and observability.
- `IWorkspaceManager` and worktree containment/branch validation.
- Durable execution fencing: launch nonce, process identity, safe release,
  quarantine, ownership reconciliation, and restart recovery.
- `ICodexAppServerClient`, its transport, idle/process supervision, and the
  explicit child-environment allowlist.
- `IWorkTracker`/GitHub Issues as an adapter behind provider-neutral contracts.
- `WORKFLOW.md` loading and policy validation, with provider credentials kept
  outside Codex's environment.
- Filesystem sidecars and cleanup only after ownership is proven safe.

## Collapse

- `RoleBasedWorkExecutor` becomes a single supervised agent execution service;
  Manager/Developer/Reviewer turns become one Codex task objective and terminal
  result.
- Immutable workspace values become one supervisor-owned execution context that
  applies durable mutations centrally while retaining fencing checks.
- Publication, CI, and feedback outcomes become external agent activity plus a
  bounded supervisor result, rather than orchestrator phase transitions.
- GitHub-specific publication/CI/review calls remain adapters only; the core
  scheduler consumes task facts and runtime outcomes, not PR concepts.

## Delete after migration

Delete only once references and recovery dependencies are removed:

- `GitWorkPublisher` as an orchestrator-owned commit/push/PR authority.
- `CiShepherd`, `CodexCiRemediator`, and CI remediation rounds.
- `ReviewFeedbackShepherd`, `CodexReviewFeedbackRemediator`, and feedback
  remediation state.
- Publication phases (`Authorized`, `Committed`, `Pushed`,
  `PullRequestCreated`) and their sidecar fields.
- Role handoff contracts, role-specific terminal schemas, and telemetry that
  exists only for those state machines.
- Exact-head PR/CI/feedback durable state and configuration used exclusively by
  those components.

Deletion is explicitly deferred until the single-agent runtime and recovery
tests replace the behavior; this document is not permission to remove code in
Stage 1.

## Proposed core boundaries

```text
ITaskProvider       discovery, eligibility facts, claim refresh
IWorkspaceProvider  isolated workspace lifecycle and fencing
IAgentRuntime       Codex App Server lifecycle and terminal result
IExecutionSupervisor scheduling, ownership, cancellation, retry, recovery
```

GitHub Issues, Git worktrees, and Codex App Server implement these boundaries.
No speculative adapters or HTTP/Studio control surface are introduced here.

## Durable invariants

1. At most one active execution owns a workspace.
2. Recovery must prove the prior nonce/process identity before reuse; ambiguous
   ownership quarantines the workspace.
3. Child processes receive only the minimum approved environment; provider
   credentials are never inherited or persisted in prompts/logs.
4. A terminal outcome is persisted only with the authoritative execution
   context returned by the durable mutation.
5. Human merge remains external policy; the daemon never merges by default.

The simplified lifecycle is:

```text
Queued → Running → Waiting | Retryable | Completed | Blocked | Quarantined
```

## Tests made obsolete or replaced

The following existing areas become obsolete with the corresponding deletion:

- `SingleAgentWorkExecutorTests` role-order, Manager/Developer/Reviewer
  contract, review-round, publication, CI, and feedback-loop cases.
- `GitWorkPublisherTests` publication phase/recovery and PR identity cases.
- `CiShepherdTests`, `CodexCiRemediator` tests, and CI remediation prompt tests.
- `ReviewFeedbackShepherdTests` and feedback remediator/provider lifecycle tests
  that assert orchestrator-owned remediation.
- Startup tests asserting publication/CI/feedback phase replay.

They should be replaced by focused tests for eligibility/claim concurrency,
workspace fencing, Codex launch/terminal outcomes, cancellation, retry,
quarantine, and restart recovery. Provider adapter tests remain where they
protect adapter correctness, but must not recreate the deleted orchestration.

## Stage 1 validation boundary

Only this document is added in Stage 1. No runtime, workflow, acceptance
artifact, or Stage G state is modified. Later stages must keep the repository
buildable and stop for review after each deletion/refactor stage.
