# Agent Orchestrator operations

The Agent Orchestrator writes a local, read-only operator surface under the
configured workspace root:

```text
<workspace-root>/.automaton-agent-orchestrator/operator/
  status.json
  events.jsonl
  events.jsonl.1
```

These files are daemon-owned and outside agent-visible Git worktrees. They are
diagnostic state, not product audit history, and must not be edited to control
the daemon.

## Health and status

`status.json` is replaced atomically on the configured interval. Schema version
1 includes:

- daemon state: `starting`, `reconciling`, `running`, `stopping`, or `degraded`;
- whether startup reconciliation completed and polling is enabled;
- repository identity and generation timestamp;
- current scheduler run state and attempt;
- current stage and role from the latest structured lifecycle event;
- workspace key/path, branch, PR URL, and exact published SHA;
- execution-ownership, CI, and human-feedback state and observation timestamps;
- retry time and bounded blocked/recovery reason.

`healthy: false` means the operator projection is degraded or durable workspace
state could not be verified. It does not override the authoritative workspace
sidecars or process-fencing state. During startup, `startupReconciled: false`
means work polling remains closed.

## Events

`events.jsonl` contains one bounded JSON object per lifecycle transition. Event
types cover reconciliation, run start/terminal state, retries, roles,
publication, CI, feedback, and shutdown. The file rotates to `events.jsonl.1`
at `observability.maximum_event_file_bytes`; only one prior segment is retained.

Messages and metadata are bounded. Secret-like metadata names are persisted as
`[REDACTED]`. Tokens, authorization headers, environment values, prompts,
transcripts, raw provider payloads, and CI logs must never be emitted.

Useful checks:

```powershell
Get-Content <workspace-root>/.automaton-agent-orchestrator/operator/status.json
Get-Content <workspace-root>/.automaton-agent-orchestrator/operator/events.jsonl -Tail 50
```

## Configuration

```yaml
observability:
  enabled: true
  snapshot_interval_ms: 10000
  maximum_event_file_bytes: 5242880
```

Disabling observability suppresses the operator files but does not change
scheduling, recovery, fencing, publication, CI, or feedback behavior.
