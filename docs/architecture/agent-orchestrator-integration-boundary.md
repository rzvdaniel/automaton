# Agent Orchestrator integration boundary

## Decision

The Agent Orchestrator remains a standalone daemon. Automaton API and Studio are optional read-only consumers; they are not required for configuration, credentials, scheduling, durable state, recovery, publication, CI, feedback, health, or operation.

```text
GitHub / Jira / future work providers
                 |
                 v
        Agent Orchestrator
          own configuration
          own credentials
          own durable state
          own recovery and fencing
          own operator surface
                 ^
                 | versioned, optional, read-only contract
                 |
        Automaton API / Studio adapter
```

Stage 14 introduces the provider-neutral `IOrchestratorStatusQuery` contract and a local `FileOrchestratorStatusQuery` adapter over Stage 12's atomic snapshot. This is intentionally not an HTTP server, Studio API endpoint, database projection, event bus, or UI. It proves that an adapter can consume stable orchestration DTOs without referencing GitHub DTOs or Studio models.

## Versioned read contract

`OrchestratorStatusQueryResult.schemaVersion` versions the external integration envelope independently from the internal sidecar and operator-file schemas. Consumers must reject unsupported versions. Availability is explicit: missing, malformed, oversized, unreadable, or unsupported local status produces a bounded `unavailable` result rather than invented healthy state.

The snapshot exposes only normalized orchestration concepts:

- daemon health, reconciliation, generation time, repository identity, and build version/revision;
- stable work-item ID, daemon run state, attempt, current stage/role, retry time, and activity time;
- workspace key/path, daemon branch, pull-request URL, and exact head SHA;
- normalized CI, human-feedback, and execution-ownership state;
- bounded/redacted blocked, failed, or quarantined reason and observation timestamps.

It does not expose provider payloads, GitHub workflow/check identifiers, prompts, transcripts, credentials, process IDs/nonces, raw logs, or mutable orchestration objects. Work-item identity is an opaque stable string: its provider does not select the repository, publication, CI, or feedback provider.

## Adapter and trust model

The shipped adapter is local and read-only. It reads the daemon-owned atomic status file with a size bound and maps it into new integration DTOs. A same-host Automaton API adapter may run with narrowly scoped filesystem read permission, but must not run as `automaton-agent`, read `secrets.env`, read Codex authentication, traverse worktrees, or write sidecars. File access failure must affect Studio visibility only; it cannot affect daemon execution.

A future remote adapter should expose the same DTOs over authenticated TLS. Use a dedicated machine identity with read-only `orchestrator.status.read` scope, audience binding, short token lifetime, replay-resistant validation, request limits, and audit logging in the serving adapter. Do not reuse Studio user PATs, GitHub credentials, daemon provider credentials, or Codex authentication. Network loss and Automaton API/Studio downtime must never stop polling, execution, recovery, or local operator output. Stage 14 opens no inbound port.

Workspace paths and reasons are operator-sensitive even after secret redaction. A remote/Studio adapter may further reduce them according to authorization; it must never enrich them by reading raw sidecars or provider data. Build identity and exact SHA remain intact for stale-data and deployment diagnosis.

## Future commands

No control command is implemented in Stage 14. Future commands such as pause discovery, resume discovery, retry, quarantine acknowledgement, or graceful drain require a separately versioned command contract with explicit authorization, idempotency key, durable audit record, current-state preconditions, and fail-closed fencing behavior. A read credential must never authorize control. Commands must not permit ownership clearing, arbitrary path operations, force push, merge, issue closure, or bypass of reviewer/CI/human authority.

## Compatibility rules

- Additive nullable fields may be introduced within a schema version; removals, semantic changes, or required-field changes require a new version.
- Integration DTO string values are orchestrator-owned normalized values, not Studio enums or provider-native strings.
- Consumers use `GeneratedAtUtc`, build identity, and exact head SHA to detect stale observations; they do not infer liveness from transport success alone.
- The local file adapter supports the current Stage 12 operator schema. Internal sidecar evolution stays private and is projected by the daemon before integration consumption.
- Standalone daemon operation is the acceptance baseline. An adapter can disappear permanently without changing orchestration correctness.

## Deferred Studio adapter

After controlled production acceptance, an Automaton API module may consume `IOrchestratorStatusQuery` or its remote equivalent and expose an authenticated read-only endpoint to a small Studio operator view. That later work must use Automaton admin documentation and authorization conventions. It is deliberately deferred so Stage 14 does not introduce Studio coupling or a UI before the daemon is proven in production.
