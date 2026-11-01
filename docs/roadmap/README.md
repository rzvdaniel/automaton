# Automaton roadmap

Updated: 31 October 2026.

## Product

Automaton is a self-hosted workspace where individuals and small teams deploy, share and run web apps and automations on machines they own or rent. Customers choose and pay for their infrastructure. Automaton supplies deployment, access control, routing, jobs and operational visibility with predictable infrastructure costs.

Each machine runs **one management Runner**, responsible for machine management and application orchestration. User applications, builds and Actions run in separate containers. The Runner is a trusted management service, not a shared process in which users execute arbitrary code.

Automaton maintains its own server with its own building blocks where possible: premade maintenance apps with Actions and schedules, rather than task-specific platform code. See [self-maintenance](05-self-maintenance.md).

## Plans

Each plan is a numbered file. The number is a permanent ID, not a priority: new plans take the next number and are never renumbered. This table lists open plans first, in priority order, then completed ones. When a plan's status changes, update its row here and the status line at the top of its file, with evidence.

| # | Plan | Status |
| --- | --- | --- |
| 05 | [Self-maintenance](05-self-maintenance.md) | **In progress**: app secrets and shared vault done; next app permissions, then encrypted backup download, Admin → Maintenance, off-site and app-data backups |
| 04 | [.NET 10 LTS](04-dotnet-10.md) | Postponed (decided 31 October 2026). .NET 9 support ends 10 November 2026; after that it gets no security fixes |
| 03 | [Enhancements](03-enhancements.md) | Ongoing list: persisted runtime logs, real-time connections, failing tests on main |
| 01 | [Self-hosted core](01-self-hosted-core.md) | Done (M1–M4). Open before team use: two-user isolation and combined restart/replay acceptance |
| 02 | [App store](02-app-store.md) | Done (server 0.1.12). Follow-up ideas only |

## Working rules

Keep verification proportional to changed behavior. Run focused contract/unit checks for decision logic, Linux fixtures for filesystem/container behavior, and real-database checks when changing migrations, SQL predicates or concurrency. A build alone cannot establish those behaviors.

For runtime changes, follow the repository-required affected-project and full-solution builds and run the end-to-end acceptance relevant to the plan. Record commands, results and unavailable checks in the implementation PR, not in the roadmap. Mark a plan or step done only with its evidence (PR, release, validation) and remaining limitations. Production credentials, deployments and data changes remain separate operations.

## Deliberately out of scope

Public multi-tenant hosting, automatic cloud capacity purchasing, managed VM/microVM creation, Kubernetes, cross-region failover, distributed storage, a replacement message bus, a new workflow engine and a custom agent orchestrator. Private-only/offline and ARM installation are not the supported quickstart. Optional provider integrations can be considered after the self-hosted path is useful and simple to operate.
