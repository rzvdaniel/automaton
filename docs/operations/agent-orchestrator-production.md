# Agent Orchestrator production deployment

Deploy the .NET 9 Linux service under systemd, not a terminal multiplexer. The
service account needs only its immutable release, trusted workflow,
canonical checkout, workspace root, and isolated Codex home:

```text
/opt/automaton-agent-orchestrator/releases/<version>
/etc/automaton-agent-orchestrator/WORKFLOW.md
/etc/automaton-agent-orchestrator/secrets.env
/var/lib/automaton-agent-orchestrator/repository
/var/lib/automaton-agent-orchestrator/workspaces
/var/lib/automaton-agent-orchestrator/codex
```

These paths must be disjoint. The trusted workflow and secrets are host-owned;
the service account owns the checkout, workspaces, sidecars, and operator logs.
Use one explicit GitHub token for the issue tracker and agent GitHub capability,
provided through environment references. Codex authentication lives only in the
isolated `CODEX_HOME`.

The service validates Linux mode, GitHub issue tracking, credentials, repository
identity, path separation, Codex authentication, and executable availability at
startup. A configuration failure exits non-zero; let systemd own restart policy.

Use `systemctl status automaton-agent-orchestrator`, `journalctl -u
automaton-agent-orchestrator`, and the operator files under the workspace root
to investigate. Confirm reconciliation and the reported build revision after a
deploy. Stop with `systemctl stop`; do not kill worktrees or clear ownership by
hand.

Human merge is mandatory. The daemon creates/updates a draft PR, can move the
exact Reviewer-approved head to ready-for-review, and then stops. It never
approves, merges, force-pushes, waits for CI, or manages CI/review-feedback
state.
