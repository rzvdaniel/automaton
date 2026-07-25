---
name: runner-debugging
description: Use when debugging or changing Automaton runners, runner registration, SignalR connection behavior, backend app execution, runner API keys, hosted runner linking, or runner-side deployed files.
---

# Runner Debugging

Use this skill when touching:

- `src/Automaton.Runner/**`
- runner registration
- SignalR client connection/reconnection
- runner API keys
- backend app deployment/execution
- hosted runner linking
- runner Docker configuration
- runner logs and diagnostics

## Mental model

The runner executes backend workloads outside the hosted frontend. Studio/API coordinates work and runner identity through runner-scoped credentials and SignalR communication.

Backend app files deployed to a runner are runner-side runtime files. Do not confuse them with editable Studio source.

## Rules

- Never log raw runner API keys or secrets.
- Keep runner names and hosted runner linking stable.
- Runner registration should link back by the expected runner identity, such as `AUTOMATON_RUNNER_NAME` where used.
- Do not pre-create duplicate runner rows for hosted runners.
- Preserve reconnect behavior unless intentionally changing it.
- Keep diagnostics actionable but redacted.
- Keep app backend files isolated per app/user as the current architecture requires.
- Treat App Action `ExecutionId` as an idempotency key. Persist Runner acceptance before starting the process, return the same in-flight or terminal result for duplicates, and never restart a process when a persisted non-terminal acceptance makes the outcome ambiguous.
- Before reusing or deleting an Automaton-owned Docker volume, inspect it and fail closed unless its managed, resource, owner, and storage-scope labels match the server-derived identity. A deterministic name alone is not proof of ownership.
- Do not mount one volume root at multiple unrelated container paths. Mount one storage root and map workspace, logs, home, and system configuration to distinct subdirectories or use separately labeled deterministic volumes.
- Make persistent-data disposition explicit for destructive Runner lifecycle operations. Preserve and delete paths must be distinct in the API contract, and permanent deletion requires stronger confirmation.

## Debug checklist

When investigating runner issues, check:

- runner process/container is running
- runner has the expected server URL
- runner has a valid runner-scoped API key
- API accepts the runner key scope
- SignalR connection is established or reconnecting
- runner name matches the hosted runner record when applicable
- backend app files exist in the expected runner-side directory
- execution errors are visible without leaking secrets

## Verification

Run:

```bash
dotnet test
```

For UI/admin hosted runner changes, also run:

```bash
cd src/automaton-studio-vue
npm run type-check
npm run build
```

Read:

```txt
.agent/harness/verification.md
```
