# Automaton-native Codex Agent Orchestrator

## Status and decision

**Status:** Proposed architecture, Stage 0. This document deliberately makes no
runtime implementation change.

Automaton will build a .NET, long-running orchestration service inspired by the
language-agnostic [OpenAI Symphony specification](https://github.com/openai/symphony/blob/main/SPEC.md).
It will not port or depend on Symphony's experimental Elixir implementation.

The architectural boundary is intentional:

```text
Automaton Agent Orchestrator: work lifecycle, durability, isolation, processes
Codex agents:                  software-engineering judgement and execution
```

In particular, the orchestrator is not a general-purpose workflow engine and
does not encode implementation, review, or CI-fix decisions that belong in
repository policy and Codex agent instructions.

## Repository findings

Automaton already has a suitable .NET Worker host pattern: `Automaton.Worker`
uses `BackgroundService`, scoped service resolution, Serilog, configuration
binding, graceful host cancellation, and CAP-based background processing. It is
currently responsible for database/RabbitMQ-backed product operations such as
runner reconciliation and action scheduling.

The repository also has a local PowerShell agent harness under `.agent/`.
Its role separation (orchestrator, developer, reviewer), compact hand-off
artifacts, bounded reviewer loop, and recovery files are useful policy and UX
input. It uses `codex exec` and local `AgentRuns/current` state, however, so it
is not a safe basis for a durable multi-workspace daemon. The new service should
eventually replace only the relevant orchestration concerns, not silently alter
the local harness.

GitHub support currently exists in the Studio API, including encrypted
user-scoped PAT storage (`UserGitHubToken`), `GitHubTokenService`, and
`GitHubService`. Those are designed for an authenticated Studio user and must
not be passed wholesale to a daemon or inherited by Codex processes. The
orchestrator needs a separately configured host-side integration identity.

Production has a single in-place `Automaton.Worker` alongside blue/green API,
Studio, and Website services. A process that clones repositories, starts Codex,
and may later push branches has materially different filesystem, network, and
credential authority from that Worker.

## Decision: dedicated service and project layout

Create a separate process in later stages, tentatively named
`Automaton.AgentOrchestrator`. Do not embed it in `Automaton.Worker`, `Api`, or
`Runner` merely to avoid another executable. This separates the orchestrator's
Git/Codex/workspace capability from the CAP and database credential set used by
the product Worker. It may share focused, dependency-light libraries with
Automaton only where the dependency direction remains clean.

Proposed layout (to be created incrementally, not in Stage 0):

```text
src/
  Automaton.AgentOrchestrator/                 # generic host and composition root
    Configuration/
    Hosting/
    Orchestration/
    Observability/
  Automaton.AgentOrchestrator.Core/            # domain and provider-neutral contracts
    Workflows/
    WorkTracking/
    Workspaces/
    Runs/
    Codex/
  Automaton.AgentOrchestrator.GitHub/          # GitHub Issues adapter and host-side auth
  Automaton.AgentOrchestrator.Codex/           # versioned App Server JSON-RPC client
  UnitTests/Automaton.AgentOrchestrator.Tests/
docs/architecture/agent-orchestrator.md
```

`Core` owns immutable models and interfaces; it has no HTTP, process, GitHub,
or filesystem implementation dependency. The host owns mutable in-memory
scheduling state. Adapter projects own protocol and provider details. This
allows deterministic tests using fakes without trying to emulate GitHub or
Codex.

The exact project split should be reassessed at Stage 1 after examining the
package/dependency direction needed for YAML and process protocol handling. A
single host project with internal folders is acceptable for Stage 1 if the
separation remains enforced by interfaces and tests; no speculative package
proliferation is required.

## Components and ownership

| Component | Owns | Does not own |
| --- | --- | --- |
| Workflow loader/configuration | `WORKFLOW.md` parsing, environment-reference resolution, typed validation, last-known-good reload | Secrets or tracker-specific semantics |
| `IWorkTracker` | Candidate discovery, work-item refresh, terminal/eligibility facts | Scheduling or workspace/process control |
| GitHub Issues adapter | GitHub REST/GraphQL calls, issue/PR distinction, label/state normalization | Raw token exposure to Codex |
| Orchestrator/scheduler | Poll ticks, claims, bounded concurrency, cancellation, retries, reconciliation decisions | Engineering decisions or GitHub-specific fields |
| Workspace manager | Deterministic worktree paths, branch/worktree lifecycle, path validation, safe cleanup | Agent prompting or issue eligibility |
| Codex App Server client | Launch, JSON-RPC handshake, thread/turn lifecycle, event normalization, cancellation, process shutdown | Retry policy or tracker decisions |
| Agent session manager | Session snapshot, continuation turns, idle/process-death signals, usage aggregation | Developer/reviewer policy before Stage 7 |
| Run-state store | Reconstructable sidecar state outside the checked-out repository | A new database in the initial recovery design |
| Observability | Structured logs, redacted runtime snapshot, health/readiness signals | A large Studio UI |

The orchestrator has one authoritative, process-local active-run map keyed by
the stable tracker work ID. A work item is claimed before asynchronous workspace
or process work begins and released only by the orchestrator after a terminal,
retry, or cancellation outcome. Adapters report facts and events; they cannot
mutate scheduler state directly.

## Domain contracts

The initial normalized models are:

```text
WorkflowDefinition  raw front matter, rendered prompt template, source location
OrchestratorOptions resolved, validated runtime policy snapshot
WorkItem            provider-neutral id, display identifier, title, body,
                    labels, provider lifecycle state, URL, assignment and timestamps
Workspace           root-contained absolute path, workspace key, branch, lifecycle state
RunAttempt          work item + workspace + attempt number + timestamps + outcome
LiveSession         App Server process/thread/turn identity and last activity/usage
RunState            transient orchestration lifecycle (eligibility, claim,
                    execution phase, retry, block, handoff, completion)
```

Key interfaces proposed for implementation:

```csharp
public interface IWorkflowLoader;
public interface IWorkTracker;
public interface IWorkspaceManager;
public interface ICodexAppServerClient;
public interface IRunStateStore;
public interface IClock;
```

`IWorkTracker` exposes candidates and refresh-by-stable-ID; it never exposes
GitHub issue DTOs to the scheduler. `ICodexAppServerClient` returns a stream of
typed, version-aware session events rather than parsed CLI text. `IRunStateStore`
is filesystem-backed initially, with a replaceable contract should reconstruction
prove insufficient later.

### Provider lifecycle, external intent, and orchestration state

These are intentionally separate contracts. `WorkItem.ProviderLifecycleState`
means only the provider-native lifecycle returned by the tracker. For GitHub
Issues it is normally `open` or `closed`; it must not be overloaded with an
orchestrator phase such as `reviewing` or `retrying`.

`WorkItem.Labels` retains normalized provider labels. Configured `agent:*`
labels are durable, externally visible intent or handoff signals when useful:
`agent:ready` opts an open issue into dispatch, and later labels may express
durable outcomes such as `agent:blocked` or `agent:human-review`. They are not
required—and should not be written—for every short-lived execution transition.

`RunState` is daemon-owned and transient. It represents the richer lifecycle
needed to operate a run, including `ineligible`, `eligible`, `claimed`,
`workspace-preparing`, `working`, `reviewing`, `rework`, `retry-queued`,
`blocked`, `human-review`, `cancelled`, and `completed`. Its values are not
GitHub states or labels. This prevents a GitHub close event, a durable handoff
label, and a currently running Codex session from being represented by one
ambiguous `State` property. The scheduler derives eligibility from the current
provider lifecycle plus configured label policy, while reconciliation can stop
or hand off a transient `RunState` when that external state changes.

## Workflow contract

Each orchestrated repository will opt in with repository-owned `WORKFLOW.md`:
optional YAML front matter and a Markdown prompt body, following Symphony's
contract. The front matter will define typed sections for tracker, repository,
polling, workspace, agent execution, Codex App Server, concurrency, retry, and
reconciliation. Secrets are prohibited as literal values. A field may reference
an explicitly allowed environment variable, and configuration validation must
fail closed if it resolves empty.

The initial GitHub profile will require an explicit repository and opt-in label,
for example `agent:ready`. It will list only provider-`open` GitHub Issues in
that repository, exclude pull requests, and preserve provider metadata only
inside the adapter/normalized `WorkItem`. GitHub Issues is first because
Automaton already has GitHub user-facing integration and the target source
repository is Git-based; the abstraction keeps a future tracker possible without
coupling the orchestrator to GitHub.

Workflow reload should eventually validate a replacement before applying it;
an invalid reload retains the last known-good configuration and blocks new
dispatches with an operator-visible error. A run receives an immutable effective
configuration snapshot, so a reload cannot change an in-flight process's
tracker, credentials, hooks, or sandbox policy.

## Lifecycle

```text
discover eligible work
  -> atomically claim in the scheduler
  -> create or recover isolated workspace
  -> persist attempt/session sidecar
  -> launch and initialize Codex App Server
  -> create/resume thread and run one or more turns
  -> reconcile tracker + workspace + process state
  -> complete | queue backoff retry | block | cancel
  -> clean only a verified terminal workspace when policy allows
```

During later multi-agent stages, the manager receives the work objective and
coordinates developer/reviewer sessions through Codex-native instructions and
agent configuration. The outer lifecycle remains unchanged. A reviewer is an
independent context and has read-only workspace permission by default; a manager
cannot treat a developer self-review as approval.

## Workspace isolation and recovery

Stage 3 should use Git worktrees unless a repository-specific constraint
invalidates that choice. A configured workspace root contains one directory per
normalized work identifier, such as `GH-123-<stable-suffix>`. The canonical
repository checkout remains outside that root. Paths are normalized to absolute
paths and checked with directory-aware containment before creation, command
launch, or removal. Identifier sanitization must be collision-resistant; a
replacement character alone is insufficient.

Each workspace receives a daemon-owned, non-repository sidecar state directory
under the configured workspace root. It records the stable work ID, repository
identity, branch, worktree path, attempt state, App Server PID/start identity,
thread ID, turn ID, a durable run/launch nonce, timestamps, and redacted outcome
metadata. It must never be committed with agent changes or contain raw
credentials or unbounded transcript payloads.

### Exclusive execution ownership invariant

**At most one active agent execution may own a workspace at any time.** A
`RunAttempt` owns a workspace only after the daemon has recorded its unique
run/launch nonce and established a daemon-owned process boundary for the App
Server and every child it starts. The future implementation must use the
appropriate host mechanism—such as a process group, Windows Job Object, cgroup,
or equivalent—not a bare PID alone. The nonce binds the sidecar, the scheduler
claim, and that process ownership boundary so a recycled PID cannot be mistaken
for an earlier execution.

Before recovery or redispatch, the daemon must establish exclusive ownership. An
old execution is allowed only one of these outcomes:

1. identify it safely from its durable nonce and process boundary, then reclaim
   and resume it;
2. identify it safely, terminate its complete owned process boundary, and verify
   termination before a replacement execution starts; or
3. when ownership or termination cannot be established safely, quarantine the
   workspace and mark the run blocked for operator action.

It must never declare an unverifiable process dead merely because it cannot be
attached to, then start another agent in the same workspace. Platform-specific
fencing, descendant-process discovery, and termination verification are deferred
to the workspace, App Server, and restart-recovery stages; this invariant is
non-negotiable for all of them.

Restart recovery is tracker-and-filesystem first:

1. enumerate only validated sidecars beneath the configured workspace root;
2. refresh their tracker work items;
3. remove only safely contained terminal workspaces under configured cleanup
   policy;
4. establish exclusive execution ownership using the prior run nonce and daemon
   process boundary; safely reclaim, terminate-and-verify, or quarantine as
   required by the invariant above;
5. only after exclusive ownership is established, resume a valid work item from
   its recorded thread when the App Server supports it, otherwise start a fresh
   turn with a compact recovery summary;
6. rebuild claims/retry scheduling from current tracker eligibility and sidecars.

There is no orchestrator database in the initial design. Filesystem sidecars are
the minimum durable supplement because tracker state and a Git worktree cannot
reconstruct App Server thread identity or retry timing safely. A future durable
store needs an explicit lease/idempotency design before multiple daemon replicas
are allowed. Initial deployment is one active daemon per workflow/repository.

## Codex App Server boundary

The client will launch the configured `codex app-server` in the assigned
workspace with a minimal environment. It will speak the current, versioned
JSON-RPC protocol over the supported transport, not parse `codex exec` output.
The client must initialize once, send `initialized`, start or resume a thread,
start/steer/interrupt turns, and consume structured notifications until terminal
turn status. It records thread and turn IDs and cleanly terminates the child
process during cancellation and host shutdown.

OpenAI's current App Server documentation defines the protocol as the source of
truth: it requires the initialization handshake before other requests; models
work as threads containing turns and items; turns stream notifications and end
with `turn/completed`. The documentation notes the SDK as the preferred interface
for CI/job automation, but this design deliberately targets App Server because
the requested product requirement needs structured thread/session control,
streamed events, approvals, and multi-turn continuation. Stage 4 must validate
the installed schema with `codex app-server generate-json-schema` and keep the
protocol adapter versioned and isolated so it can evolve independently.

### Stage 4 protocol baseline

Stage 4 is validated against the schema generated by Codex CLI `0.147.0` with
`codex app-server generate-json-schema --out <directory>`. The adapter uses the
stable V1 `initialize` request and V2 thread/turn methods: `thread/start`,
`thread/resume`, `turn/start`, and `turn/interrupt`. It consumes JSON-lines
responses and notifications including `turn/started`, `item/started`,
`item/completed`, `thread/tokenUsage/updated`, and terminal `turn/completed`.
Wire method names and JSON shapes remain confined to the Codex adapter; the
orchestrator receives bounded typed session events and IDs instead of raw lines
or unbounded transcripts.

Each client is permanently bound to its normalized Stage 3 workspace path.
`thread/start` always uses that path, and `thread/resume` supplies it as the
protocol cwd override. Both operations validate the required top-level `cwd` in
their 0.147.0 response and reject a missing, relative, or mismatched path.
Callers cannot provide a different thread working directory.

The configured `codex.turn_timeout_ms` is interpreted at this boundary as a
meaningful-activity idle timeout. Responses, notifications, item activity, and
usage updates refresh it, so healthy long-running turns are not killed merely
for exceeding a total wall-clock duration. Cancellation and idle timeout first
request `turn/interrupt`; session disposal closes stdin, waits briefly for clean
exit, and then kills the full process tree if required.

The process starts with `UseShellExecute=false`, an argument list, the workspace
as its current directory, and a minimal allowlisted environment. Tracker tokens
and Automaton database/runner secrets are neither inherited nor accepted as
explicit child settings. Stage 4 records PID, start time, and the Stage 3 launch
nonce. Stage 6 strengthens this boundary as described below.
Unexpected exit or malformed protocol therefore quarantines workspace ownership
instead of clearing it and risking a duplicate execution.

### Stage 5 single-agent execution baseline

Stage 5 replaces the production no-op executor with one provider-neutral
single-agent execution flow. An immutable workflow snapshot supplies the
configured canonical repository root and identity, the workspace manager
creates or recovers the isolated worktree, and the workspace-bound App Server
client starts one new thread for the run. Continuation turns reuse that thread;
restart-time thread recovery remains deferred to Stage 6.

The initial prompt is a deterministic envelope that separates orchestration
metadata, untrusted issue body text, and repository-owned workflow guidance. It
names the assigned worktree and branch, requires applicable `AGENTS.md` and
repository-local instruction discovery, forbids operations outside that
worktree, and delegates investigation, implementation, testing, repair, and
self-review decisions to Codex. Tracker credentials and resolved provider
configuration are not included in the prompt or child environment.

Each completed turn must end with one strict line:

```text
AUTOMATON_RESULT: {"status":"CONTINUE|COMPLETED|BLOCKED","summary":"bounded text"}
```

`CONTINUE` starts another turn on the same thread up to `agent.max_turns`,
`COMPLETED` maps to scheduler success, and `BLOCKED` or malformed output maps to
non-retryable manual attention. A failed/interrupted turn or idle timeout is
retryable only after clean process shutdown releases matching ownership.
Protocol/process ambiguity remains quarantined and is never immediately reused.
Workspaces are preserved in `ready`, `retryable-failure`, or `failed` lifecycle
state; Stage 5 neither closes issues nor pushes branches or creates pull
requests.

Because Stage 5 deliberately leaves provider lifecycle and opt-in labels
unchanged, the scheduler keeps a daemon-local stable-work-item-ID suppression
set for `completed`, `blocked`, and `cancelled` terminal runs. This prevents the
same still-eligible issue from being immediately dispatched again while normal
retry-queued work continues unchanged. Stage 6 reconstructs this suppression
from daemon-owned sidecar state after a restart; the in-memory set remains the
live scheduler representation.

### Stage 6 restart recovery and execution fencing

Startup polling is gated until the daemon has enumerated and validated every
owned sidecar/worktree, resolved active execution ownership, refreshed the
associated tracker items, and reconstructed terminal, retry, and ineligible
scheduler state. Tracker-wide refresh failure keeps the gate closed; an
individual quarantined workspace is restored as blocked without preventing
unrelated safe work from running.

Sidecar schema version 2 keeps workspace lifecycle separate from execution
ownership and adds bounded scheduler recovery state. It durably records the
last completed and next attempt, optional retry-not-before time, bounded reason,
root PID and process start time, launch nonce, optional Unix process-group ID,
fencing kind, and execution start time. Writes use a same-directory temporary
file that is flushed and closed before replacement. Future schemas are rejected;
legacy sidecars are migrated conservatively to blocked, and legacy active
ownership without sufficient identity is quarantined. Prompts, transcripts,
credentials, and OS handles are never stored.

On Windows, new App Servers are assigned to a kill-on-close Job Object when
available and otherwise use the existing process-tree boundary. Job handles are
live-daemon resources, not durable restart identity. On restart the daemon
compares the persisted PID and exact start time before terminating the matching
process tree. A missing root is sufficient restart proof only for a recorded
`windows-job` execution, because closing the daemon-owned kill-on-close handle
also fences descendants. A `portable-process-tree` execution with a missing root
remains ambiguous: its descendants cannot be proven absent after restart, so the
workspace is quarantined. On Linux, App Servers lead a new session/process
group; PID and start time remain the primary identity and the persisted
process-group ID scopes termination. A missing Linux root is dead only when the
process group is proven absent; an existing or uninspectable group is ambiguous.
Unknown fencing kinds also fail closed. PID reuse never authorizes killing the
new process. Inspection
failure, incomplete identity, failed verified termination, or any descendant
ownership ambiguity fails closed to quarantine. Unsupported platforms cannot
clear unresolved active ownership after restart.

Stage 6 preserves workspace continuity, not live Codex session continuity. It
never adopts old stdio, reconnects to an App Server, or resumes an in-progress
turn. A matching old execution is terminated and verified dead (or proven
already dead), ownership is cleared with its matching nonce, and any eligible
retry starts a fresh App Server and thread in the preserved worktree.

The durable outcome reconstructs completed, blocked, and cancelled suppression.
A retryable failure is refreshed against current provider lifecycle and labels,
then resumes at its persisted next attempt and optional due time; exceeding the
configured attempt limit becomes blocked. The executor persists the intended
attempt before launching and persists the completed attempt/next state before
publishing its workspace lifecycle transition. Ambiguity therefore favors a
blocked or quarantined run over resetting an attempt or launching twice.

Recovery deliberately uses no database: authoritative inputs are tracker state,
validated daemon-owned sidecars, Git worktree registration, and OS process
inspection. Graceful shutdown likewise clears active ownership only after the
owned process boundary is verified dead; shutdown ambiguity remains quarantined.

### Stage 7 role-based engineering execution

Stage 7 replaces the single reasoning context with three explicit roles in a
fixed sequence: Manager, Developer, then Reviewer. The outer daemon still owns
tracker discovery, concurrency, retries, workspace and execution ownership,
restart recovery, App Server lifetime, cancellation, and durable outcomes. Role
agents never manipulate sidecars or scheduler state.

One workspace ownership lease and one workspace-bound App Server process cover
the complete run. Each role starts a distinct Codex thread; continuation turns
reuse only that role's thread. Manager plans without editing, Developer performs
the implementation, and Reviewer independently evaluates the actual worktree.
Role thread/session continuity does not survive restart, while Stage 6 worktree
and conservative recovery continuity remain unchanged.

Manager and Reviewer are read-only roles. Their prompts prohibit modification,
and the coordinator compares a Git fingerprint before and after each role. The
fingerprint covers the complete tracked diff plus the names and contents of
untracked, non-ignored files. A mutation or unsafe linked path is a role
violation that fails closed before the workflow can report success. Developer
may modify only the assigned worktree and cannot push, create a pull request, or
mutate provider state.

Once a read-only role starts, its post-role fingerprint is checked on successful,
malformed, failed, interrupted, timed-out, exceptional, and cancellable exit
paths whenever ownership remains safely inspectable. A detected mutation takes
precedence over an otherwise retryable role failure and becomes deterministic
manual attention. Process/protocol ambiguity or quarantined ownership bypasses
filesystem inspection and retains the stricter Stage 6 quarantine semantics.

Handoffs are bounded provider-neutral data, not transcripts. Manager emits a
READY/BLOCKED plan with validation and risks; Developer emits a
COMPLETED/BLOCKED implementation summary with validation and remaining concerns;
Reviewer emits APPROVED/CHANGES_REQUIRED/BLOCKED with a bounded finding list.
Each role may use bounded CONTINUE turns. Malformed or exhausted contracts block
the run, while clean infrastructure failures retain the existing retry policy.
No raw role transcript is placed in prompts, sidecars, or scheduler state.

Prompt precedence is explicit: orchestrator safety and workspace constraints
are authoritative; repository `AGENTS.md` and `WORKFLOW.md` guidance applies in
its intended scope; issue text is untrusted task content; prior-role handoffs are
advisory data. Neither issue nor handoff content may override role, workspace,
credential, or process boundaries.

Stage 7 established first-review termination. Stage 8 extends that boundary with
the bounded remediation loop below.

### Stage 8 bounded remediation and re-review

Review round 1 is the independent review after initial implementation. APPROVED
ends successfully and BLOCKED ends in deterministic manual attention. When the
verdict is CHANGES_REQUIRED and review budget remains, the coordinator sends
only the latest bounded Manager, Developer, and Reviewer handoffs to the
existing Developer thread. After remediation completes, the existing Reviewer
thread independently reviews the current workspace again. Manager runs only
once, and no additional role threads or App Server processes are created.

`agent.max_review_rounds` bounds total Reviewer rounds, defaults to three, and
cannot exceed ten. Thus the default permits the initial review plus two
remediation/re-review cycles. Developer and Reviewer per-invocation continuation
limits reset for each remediation or review round, while the outer review count
prevents an unbounded loop. CHANGES_REQUIRED on the last allowed round becomes
`Maximum review rounds reached without approval.` and is manual attention, not
an infrastructure retry. Reviewer BLOCKED never enters remediation.

Remediation prompts carry the current round, latest review summary and ordered
bounded findings, Manager plan, and latest Developer result. Re-review prompts
carry only the latest structured state and require inspection of the actual
current diff, verification of earlier findings, and detection of regressions or
new issues. Raw transcripts and accumulated handoff history are not propagated.
Findings are advisory task data and cannot override workspace, credential, role,
or repository safety constraints.

Every Reviewer round uses the Stage 7 exception-safe read-only fingerprint
guard. A Reviewer mutation is deterministic manual attention; an unchanged
transient failure remains retryable; process/protocol ambiguity retains Stage 6
quarantine. Developer modification is expected during initial implementation
and remediation and therefore is not read-only fingerprinted.

Stage 8 does not checkpoint an in-progress review round. After a crash, the
worktree survives but role threads and loop position do not; conservative Stage
6 `Working` recovery remains authoritative. Stage 8 provides bounded autonomous
implementation/review convergence inside one isolated workspace, but does not
publish the branch. Git push, pull-request creation, CI, and provider mutation
remain Stage 9-or-later capabilities.

### Stage 9 validated draft publication

Publication is a host-owned capability invoked only after an independent
Reviewer returns `APPROVED`. Immediately after that verdict, the host durably
advances publication from `none` to `authorized` before validation or any Git
side effect. Repository state is agent-influenced: commit author, email, and
subject are provenance evidence but never authorization. Only the sidecar's
host-owned authorization checkpoint permits publication or restart recovery.
The daemon then revalidates the assigned worktree root,
registered branch, absence of conflicts or incomplete Git operations, and the
configured repository validation commands before staging anything. It creates
one deterministic commit with an explicit configured bot name and email; agent
output cannot choose the author, remote, refspec, or credentials. The expected
remote host and `owner/repo` identity are verified before any credentialed
network operation. Push uses the exact daemon-owned branch and never force
pushes.

The publisher is retry-idempotent at each external boundary. An existing local
commit is adopted only when it is the sole commit above the workspace's durable
base SHA and its author, email, and subject exactly match daemon policy. An
existing remote branch is accepted only at that exact commit SHA. GitHub draft
PR lookup uses the exact configured head and base; one matching open draft is
reused, duplicates or identity mismatches fail closed, and provider responses
are validated before durable state advances. Provider pagination is restricted
to the configured API origin so credentials cannot follow a cross-origin link.

Sidecar schema v4 records only bounded publication recovery metadata: base
commit provenance, publication phase, commit SHA, remote branch, and draft PR
number/URL. A crash between an external success and sidecar persistence is
recovered by authoritative Git remote or GitHub lookup. Older workspaces without
base provenance cannot be guessed safe for publication. Validation subprocesses
run in the assigned workspace with a minimal non-secret environment; GitHub
credentials remain host-side and are never placed in role prompts or Codex child
configuration.

The ordered phases are `none`, `authorized`, `committed`, `pushed`, and
`pull-request-created`; sidecar transitions cannot skip authorization or move
backward. Schema v3 committed/pushed/PR records migrate as authorized history
because those phases were written only by the Stage 9 host publisher, while
older or generic `working + none` records remain unapproved.

Startup distinguishes generic interrupted execution from host-authorized
publication. `working + none` keeps Stage 6's conservative manual-attention
behavior even when Git metadata perfectly resembles a daemon commit. After
ownership has been
proved ended and the tracker item is refreshed as eligible, an exact daemon
authorization allows the publisher to resume only commit/push/PR
reconciliation; Manager, Developer, and Reviewer are not rerun. `authorized`
may safely create the commit after a pre-commit crash, or recognize the exact
daemon commit when its `committed` write was missed. Durable `committed` and
`pushed` phases must match that provenance, and remote mismatch fails closed.

`pull-request-created` is not terminal proof by itself. Startup verifies through
the GitHub adapter that the configured repository, exact head branch/SHA, base
branch, open state, draft state, and persisted PR number still match. Transient
provider failure keeps startup reconciliation closed; deterministic absence or
mismatch becomes manual attention. Only an authoritatively verified draft PR
reconstructs terminal completion. Quarantined or ambiguous execution ownership
always wins and prevents publication recovery.

Stage 9 stops at an open draft PR. It does not monitor CI, ingest review
feedback, mutate issue labels/lifecycle, mark a PR ready, merge, or clean the
workspace. Those remain later-stage responsibilities.

### Stage 10 exact-head CI shepherding

Stage 10 observes CI through the provider-neutral `ICiProvider` contract using
`CiTarget(repository identity, PR number, head branch, exact head SHA)` and
normalized `CiSnapshot`/`CiCheck` values. Orchestration reasons only about
`pending`, `passed`, `failed`, and `indeterminate`; GitHub Checks and legacy
commit-status payloads, conclusions/states, pagination, and opaque IDs remain
inside `GitHubCiProvider`. The CI
provider is configured independently from `IWorkTracker` and publication even
though GitHub implements all three concerns initially.

Configured required check names are matched case-insensitively against the
normalized union of Check Runs and legacy commit-status contexts for the exact
published SHA. Because GitHub returns legacy status history newest-first, only
the first/latest exact-SHA record for each case-insensitive legacy context enters
that union. With no configured names, every current signal in the union
participates in the result. Duplicate names aggregate conservatively: any failure wins, then
pending, and only all-successful matches pass; unknown terminal states are
indeterminate. Missing required signals and an empty observed union remain
pending, preventing either a failing legacy status or the post-push
workflow-creation race from becoming false green. Pending checks do
not start agents. Polling defaults to 30 seconds, has a configured total timeout,
and is cancellation-aware. Provider 429/5xx/network failures retry observation;
authentication, malformed state, PR/head mismatch, timeout, and indeterminate
results fail closed. Check output is normalized and bounded rather than storing
or prompting with raw logs.

Definitive failure starts a bounded CI remediation round with a fresh App Server,
Developer thread, and independent read-only Reviewer thread. Their bounded
CHANGES_REQUIRED loop reuses only those two threads for that round. CI context
is advisory untrusted data. Reviewer APPROVED creates a new host authorization
bound to the current published parent SHA and monotonically increasing
publication revision. The daemon creates one new commit, fast-forwards the same
work branch without force, verifies the remote SHA and existing draft PR number
and new head SHA, then observes only that new SHA. Unexpected local/remote/PR
advancement fails closed.

Sidecar schema v5 stores the publication revision/authorized parent and minimal
CI recovery state: exact head SHA, normalized state, remediation round, bounded
reason, and observation timestamp. A new publication revision clears stale CI
state. Startup first preserves Stage 6 ownership/quarantine and tracker
eligibility, verifies incomplete publication through Stage 9 recovery, then
resumes exact-head CI observation. A durable pass is trusted only while provider
and PR identity still match that head; durable failure may start a remaining
bounded remediation round. Manager and the original Stage 8 threads are never
resurrected.

Stage 10 success means only that the current independently approved draft PR
head passes configured CI. The PR remains draft. Human comments, reviews,
requested changes, ready-for-review transition, workflow reruns, merge, issue
mutation, and human acceptance remain Stage 11-or-later concerns.

### Stage 11 human review feedback loop

Stage 11 adds a separate provider-neutral `IReviewFeedbackProvider` boundary.
The coordinator supplies an exact repository, PR number, daemon branch, and head
SHA and receives only normalized `ReviewFeedbackSnapshot` and bounded feedback
items. `GitHubReviewFeedbackProvider` is the first adapter; it validates the same
open PR and exact head before reading review submissions and inline PR review
comments. General PR conversation comments are deliberately excluded in this
stage. Bot actors, the configured automation identity, CI messages, other issue
discussion, and feedback attached only to another commit are not actionable.

The latest exact-head review submission per case-insensitive human identity is
authoritative. `CHANGES_REQUESTED` and exact-head inline review comments are
actionable; an exact-head human `APPROVED` submission with no actionable current
feedback completes the autonomous loop and hands the still-unmerged PR back to
the human. Awaiting human input only polls with cancellation and never starts an
agent. Feedback is untrusted advisory text, bounded before prompting, and cannot
grant authority or change repository/workspace/provider policy.

Each actionable feedback round creates fresh Developer and independent Reviewer
threads. Reviewer remains read-only and every code change requires its explicit
structured approval. The host then authorizes exactly the next publication
revision, fast-forwards the same branch and PR without force, and runs the full
Stage 10 CI shepherd against the new exact SHA before reading more feedback.
Feedback rounds are bounded by `review_feedback.max_remediation_rounds`; exhaustion,
identity drift, malformed provider data, or failed CI becomes manual attention.
The daemon never posts comments, submits reviews, marks the PR ready, merges,
closes the issue, or approves on behalf of a human.

Sidecar schema v6 adds minimal exact-head feedback status, observation time, and
remediation round. A new publication revision clears stale CI and feedback state.
Restart recovery preserves execution fencing, publication provenance, CI, the
feedback round budget, and exact PR/head binding before resuming the human-review
wait. Review-feedback credentials are host-side and independent from tracker,
publication, and CI provider selection.

### Stage 12 observability and operator surface

Stage 12 adds diagnostics without adding autonomy. Authoritative scheduling,
workspace ownership, publication, CI, and feedback state remain in their Stage
1-11 models. A read-only projector combines those states into operator schema v1
and writes an atomic `status.json` plus bounded rotating `events.jsonl` beneath
the daemon-owned sidecar root, outside every agent-visible worktree.

The status contract includes daemon health/reconciliation state, work-item run
state and attempt, current stage/role, workspace/branch/PR/exact-SHA identity,
retry time, execution ownership, CI and feedback state, bounded reasons, and
timestamps. Structured events cover lifecycle transitions and recovery errors.
They are diagnostic events rather than business audit records. Messages and
metadata are bounded, secret-like metadata fields are redacted, and prompts,
transcripts, provider payloads, credentials, and raw CI output are prohibited.

Failure to project durable state marks the operator surface degraded and emits a
warning, but it cannot weaken fencing, clear quarantine, or mutate orchestration
state. The files are an initial local contract for Stage 13 service packaging
and later Studio consumption; Stage 12 deliberately adds no HTTP service, large
UI, remote control, automatic merge, or new agent behavior.

## Trust and security posture

The daemon is a privileged automation boundary and must be opt-in and
least-privilege by default.

- `WORKFLOW.md` contains policy and allowed environment-variable names, never
  literal secrets.
- A dedicated host-side GitHub integration credential is scoped to the configured
  repository and required operations. Studio user PATs are not reused implicitly.
- GitHub tracker calls occur in the daemon. The credential is removed from every
  Codex child environment. If provider-native tracker tools are added, the daemon
  exposes narrow host-executed operations with typed inputs/results.
- Stage 9 Git write and draft-PR publication remain host-owned capabilities.
  The tracker/PR API token is not passed to agents, while Git push uses the
  separately configured host credential mechanism after remote identity checks.
- The Codex child process gets only the workspace, explicit non-secret
  configuration, its necessary Codex authentication mechanism, and an allowlist
  of safe environment variables. It does not receive production, database,
  RabbitMQ, runner, or Studio authentication material.
- Approval, sandbox, network, and user-input policy are explicit workflow/host
  configuration. Unhandled approval or input requests become a bounded blocked
  outcome, never an indefinitely silent run.
- Hooks are deferred until the workspace stage and are treated as privileged
  local code: they run with a fixed workspace cwd, timeout, restricted inherited
  environment, and documented failure semantics.
- Logs, sidecars, and status APIs redact secrets, raw prompts that may include
  secrets, and large event payloads. Diagnostic logs carry stable work/session
  identifiers and lifecycle outcomes.

## Operational model and observability

The process will follow the existing Worker conventions: .NET Generic Host,
`BackgroundService`, scoped services, Serilog structured logging, configuration
binding, and cancellation-aware shutdown. It will have its own deployment
identity, workspace volume, log retention, health/readiness endpoint or
equivalent probe, and restart policy. It must not share the existing Worker's
database/RabbitMQ credentials or deployment container by default.

The initial operator surface is structured logs plus a redacted runtime snapshot
rather than Studio UI. It should expose work identifier, phase, workspace and
branch, current role, review round, retry count, last activity, session/thread
IDs, token usage when supplied by the App Server, PR/CI status when later added,
and a human-readable blocker. Stage 12 can decide whether a small internal API
or Studio read-only view is justified.

For production packaging (Stage 13), prefer a dedicated container or system
service with a persistent workspace volume and configured secret injection. It
is intentionally not blue/green until the service has safe single-active-owner
semantics; two replicas must not claim the same issue. Deployment design must
define a drain sequence: stop discovery, cancel/allow bounded completion of
active runs, persist sidecars, terminate child processes, then exit. No
production deployment is part of this stage.

## Delivery sequence and scope

| Stage | Scope | Deliberately excluded |
| --- | --- | --- |
| 0 (this PR) | Architecture, repository/official-documentation discovery | Runtime code or UI |
| 1 | Workflow parser, validated configuration, normalized models, in-memory core loop and fakes | Real GitHub/Codex/worktree integration |
| 2 | GitHub Issues adapter with opt-in label/filtering | Agent access to raw GitHub token |
| 3 | Worktree lifecycle, branch and containment-safe cleanup | App Server protocol |
| 4 | App Server process/JSON-RPC client and controlled real smoke test | Engineering-team semantics |
| 5-6 | Single-agent execution, reconciliation and restart recovery | Manager/reviewer loop |
| 7-8 | Codex-native manager/developer/reviewer policy and autonomous review verdict loop | PR merge authority |
| 9-11 | Validation, PR/CI, and external review-feedback loops | Automatic merge |
| 12-14 | Minimal operations surface, daemon packaging, Studio design | Large Studio implementation in Stage 14 |

The initial system intentionally defers distributed scheduling, automatic merge,
multi-tracker support, an expansive UI, generic workflow DAGs, remote execution,
and a persistent orchestration database. They should be designed only after the
single-daemon, single-repository recovery contract is proven.

## Stage 1 plan

1. Create the minimal host/core boundary and a repository-owned `WORKFLOW.md`
   loader with YAML-front-matter/Markdown parsing.
2. Add typed validation for the proposed configuration sections and explicit
   environment-variable references without resolving or logging secret values.
3. Implement provider-neutral models, tracker and executor test doubles, and the
   single-authority polling/claim/retry loop with graceful cancellation.
4. Add deterministic tests for parsing, validation, eligibility, concurrency,
   cancellation, retry scheduling, and shutdown.
5. Run the new targeted test project, self-review the complete diff, and open a
   separate Stage 1 PR. Do not connect GitHub or Codex in that stage.

## Open questions to resolve at their owning stages

1. Which host-side credential mechanism can safely cover GitHub issue reads,
   branch pushes, and PR creation without giving Codex a broad token (dedicated
   GitHub App is preferred for production assessment)?
2. Which minimal App Server version/schema compatibility policy will Automaton
   support, and how will upgrades be integration-tested?
3. Should worktree creation use a mirror/cache repository for speed, and what
   retention/quota policy protects the daemon host?
4. What explicit sandbox/network policy is acceptable for the first trusted
   Automaton-maintainer repository, before customer repositories are considered?
5. Should the initial daemon operate only the Automaton repository or accept a
   configured repository allowlist? The latter is safer once configuration exists.

## References

- [OpenAI Symphony specification](https://github.com/openai/symphony/blob/main/SPEC.md)
- [OpenAI Codex App Server documentation](https://developers.openai.com/codex/app-server/)
- `.agent/docs/agent-harness-architecture.md`
- `src/Automaton.Worker/Program.cs`
- `src/Automaton.Worker/Services/HostedRunnerRegistrationReconciliationService.cs`
- `src/Automaton.Api/Services/GitHubTokenService.cs`
- `src/Automaton.Infrastructure/Entities/UserGitHubToken.cs`
- `docs/architecture/action-schedules.md`
## Stage 13: portable daemon and Linux production packaging

The orchestrator application remains platform-neutral and continues to build and run directly on Windows. Linux/systemd is the first production packaging target, so Linux filesystem locations, account permissions, resource limits, shell tooling, and service-manager behavior stay in deployment assets rather than core orchestration contracts.

Production runs as a dedicated unprivileged identity with immutable versioned releases, trusted configuration outside workspaces, durable daemon-owned state, an isolated Codex home, and separately named host-side provider credentials. Startup fails closed on missing prerequisites or overlapping trust boundaries. Graceful termination preserves Stage 6 ownership evidence before a bounded service-manager process-tree kill. Operator status includes build version/revision; packages exclude secrets and configuration and are checksum-verified before an atomic release-link switch. Rollback is explicit and must respect durable sidecar compatibility.
