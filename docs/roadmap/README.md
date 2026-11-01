# Automaton roadmap

Updated: 1 November 2026.

## Product

Automaton is a self-hosted workspace where individuals and small teams deploy, share and run web apps and automations on machines they own or rent. Customers choose and pay for their infrastructure. Automaton supplies deployment, access control, routing, jobs and operational visibility with predictable infrastructure costs.

Each machine runs **one management Runner**, responsible for machine management and application orchestration. User applications, builds and Actions run in separate containers. The Runner is a trusted management service, not a shared process in which users execute arbitrary code.

Automaton maintains its own server with its own building blocks where possible: premade maintenance apps with Actions and schedules, rather than task-specific platform code. See [self-maintenance](05-self-maintenance.md).

## Plans

Each plan is a numbered file. The number is a permanent ID, not a priority: new plans take the next number and are never renumbered. This table lists open plans first, in priority order, then completed ones.

### Design here, work and status in GitHub

- **These files** hold the design: why, architecture, security rules and decisions. Change them in reviewed PRs.
- **GitHub issues** hold the work. Each plan in progress has a parent issue; each step is a sub-issue with full context and acceptance criteria. Native *blocked by* links give the order: an issue without open blockers can start. Labels `agent:ready`, `agent:blocked`, `agent:in-progress` and `agent:human-review` show who can pick it up.
- **Status** is the issue state and the [Automaton roadmap board](https://github.com/users/rzvdaniel/projects/1) (Status, Priority, Plan). A PR closes its issue and updates the step's one-line status in the plan file; do not keep detailed progress notes in these files.
- New work found while implementing becomes a new sub-issue of the plan's parent issue, not a paragraph here.

| # | Plan | Status |
| --- | --- | --- |
| 05 | [Self-maintenance](05-self-maintenance.md) | **In progress** ([#490](https://github.com/rzvdaniel/automaton/issues/490)): app secrets and shared vault done; next app permissions (#483) and the audit log (#479) |
| 04 | [.NET 10 LTS](04-dotnet-10.md) | Postponed (decided 31 October 2026). .NET 9 support ends 10 November 2026; after that it gets no security fixes |
| 03 | [Enhancements](03-enhancements.md) | Ongoing list: persisted runtime logs, real-time connections, failing tests on main |
| 01 | [Self-hosted core](01-self-hosted-core.md) | Done (M1–M4). Open before team use: two-user isolation and combined restart/replay acceptance |
| 02 | [App store](02-app-store.md) | Done (server 0.1.12). Follow-up ideas only |

## Working rules

Keep verification proportional to changed behavior. Run focused contract/unit checks for decision logic, Linux fixtures for filesystem/container behavior, and real-database checks when changing migrations, SQL predicates or concurrency. A build alone cannot establish those behaviors.

For runtime changes, follow the repository-required affected-project and full-solution builds and run the end-to-end acceptance relevant to the plan. Record commands, results and unavailable checks in the implementation PR, not in the roadmap. Mark a plan or step done only with its evidence (PR, release, validation) and remaining limitations. Production credentials, deployments and data changes remain separate operations.

## Deliberately out of scope

Public multi-tenant hosting, automatic cloud capacity purchasing, managed VM/microVM creation, Kubernetes, cross-region failover, distributed storage, a replacement message bus, a new workflow engine and a custom agent orchestrator. Private-only/offline and ARM installation are not the supported quickstart. Optional provider integrations can be considered after the self-hosted path is useful and simple to operate.
