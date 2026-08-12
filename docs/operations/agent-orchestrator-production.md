# Agent Orchestrator production operations

Stage 13 packages the orchestrator as a framework-dependent .NET 9 `linux-x64` service. Linux/systemd is the first deployment target, not an application dependency: the application remains directly buildable and runnable on Windows, and all Linux paths, permissions, and service policy live under `deploy/` and `scripts/`.

## Trust and filesystem layout

Run the service as the dedicated, non-login `automaton-agent` account. It must not have root, sudo, Docker socket, production database, Studio API, or unrelated repository access.

| Path | Purpose | Ownership |
|---|---|---|
| `/opt/automaton-agent-orchestrator/releases/<version>` | immutable release | root, read-only |
| `/opt/automaton-agent-orchestrator/current` | atomic release link | root |
| `/etc/automaton-agent-orchestrator/WORKFLOW.md` | trusted configuration/prompt | root, readable by service |
| `/etc/automaton-agent-orchestrator/secrets.env` | host-side provider credentials | `automaton-agent`, mode `0600` |
| `/var/lib/automaton-agent-orchestrator/repository` | canonical checkout | service account |
| `/var/lib/automaton-agent-orchestrator/workspaces` | worktrees, sidecars, operator state | service account |
| `/var/lib/automaton-agent-orchestrator/codex` | isolated Codex authentication | service account, mode `0700` |

The canonical checkout, workspace root, configuration, and release directory must be disjoint. Production startup validates these boundaries and fails closed. The opt-in check is enabled only by the systemd unit; ordinary Windows/local execution is unchanged.

## Prerequisites and credentials

Install .NET 9 runtime, Git, Python 3, `tar`, `gzip`, `sha256sum`, `setsid` (util-linux), systemd, CA certificates, and a compatible Codex CLI at `/usr/local/bin/codex`. Authenticate Codex explicitly using the service account and isolated home:

```bash
sudo -u automaton-agent env HOME=/var/lib/automaton-agent-orchestrator/home CODEX_HOME=/var/lib/automaton-agent-orchestrator/codex codex login
sudo chmod 0700 /var/lib/automaton-agent-orchestrator/codex
```

Use four independently named host-side variables in `secrets.env`; never put values in `WORKFLOW.md`:

```text
AUTOMATON_TRACKER_TOKEN=...
AUTOMATON_PUBLICATION_TOKEN=...
AUTOMATON_CI_TOKEN=...
AUTOMATON_REVIEW_FEEDBACK_TOKEN=...
```

Give the tracker Issues/Metadata read access; publication Contents and Pull Requests read/write; CI Checks/commit-status/Metadata read; and feedback Pull Requests/Metadata read. Fine-grained PATs are the simplest current credential. Short-lived GitHub App tokens require an external renewal mechanism, which Stage 13 does not invent. The App Server child receives a constrained environment and does not receive provider tokens. Codex authentication is deliberately separate in `CODEX_HOME`.

Configure the canonical checkout's push authentication without embedding a token in its remote URL. Any credential helper must reference `AUTOMATON_PUBLICATION_TOKEN` by name; never persist its resolved value. Keep the checkout remote pinned to the configured repository identity.

## Build and install

From a reviewed checkout:

```bash
bash scripts/package-agent-orchestrator.sh 1.0.0
sudo install -o root -g automaton-agent -m 0640 deploy/agent-orchestrator/WORKFLOW.production.example.md /etc/automaton-agent-orchestrator/WORKFLOW.md
sudo install -o automaton-agent -g automaton-agent -m 0600 /secure/source/secrets.env /etc/automaton-agent-orchestrator/secrets.env
sudo bash scripts/deploy-agent-orchestrator.sh artifacts/agent-orchestrator/automaton-agent-orchestrator-1.0.0-linux-x64.tar.gz 1.0.0 --activate
```

The package command stamps version and Git revision, verifies that identity directly from the published assembly, writes the same identity to immutable `build-manifest.json`, excludes configuration/secrets/tests, creates a deterministic archive, and writes SHA-256. Deployment verifies checksum, archive paths, and manifest identity; refuses to overwrite an existing release; atomically switches `current`; preserves old releases; and does not start unless `--activate` is explicit. Activated installs gracefully restart the service and poll until reconciled operator status reports the exact manifest version and revision, so neither an old process nor stale status can satisfy verification. It never performs database migration and opens no inbound port.

## Health, shutdown, upgrade, and rollback

```bash
sudo systemctl status automaton-agent-orchestrator
sudo journalctl -u automaton-agent-orchestrator -n 200 --no-pager
sudo bash scripts/agent-orchestrator-health.sh
sudo systemctl stop automaton-agent-orchestrator
```

SIGTERM is delivered to the daemon first. Its existing cancellation and Stage 6 ownership persistence run before systemd's bounded final process-tree kill. Health requires an active service plus a reconciled, healthy `status.json` carrying release version and revision.

For upgrade, create a new versioned artifact, inspect configuration/schema compatibility, and run the deployment script with the new version. Never replace an existing release directory. For rollback, stop the service, atomically repoint `current` to a preserved compatible release, reload systemd, start, and run health validation. Sidecar schema is forward-written durable state; do not roll back across an incompatible schema without an explicit migration/recovery decision.

The installer intentionally does not auto-rollback after a failed start: the new release and durable diagnostics remain available for inspection, while the previous immutable release remains present for an explicit operator decision.

Quarantined or ambiguous ownership is manual-attention state. Inspect `status.json`, `events.jsonl`, journal output, sidecar ownership, and OS processes before changing it. Never delete a workspace merely from a tracker-controlled identifier and never clear ownership without proof. Retire old releases only by exact, reviewed release path while the service is stopped or running a different release.

## Acceptance repository

Before production use, exercise a disposable private repository with one `agent:ready` issue: restart during an active turn, retry recovery, publication, CI failure/remediation, human feedback, graceful stop, upgrade, rollback, and quarantine. Confirm no force push, auto-merge, issue close, credential disclosure, canonical-checkout mutation, cross-workspace execution, or second execution under ambiguous ownership. Keep the service stopped for human merge.
