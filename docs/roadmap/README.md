# Automaton roadmap

Updated: 3 November 2026.

## Product

Automaton is a self-hosted workspace where individuals and small teams deploy, share and run web apps and automations on machines they own or rent. Customers choose and pay for their infrastructure. Automaton supplies deployment, access control, routing, jobs and operational visibility with predictable infrastructure costs.

Each machine runs **one management Runner**, responsible for machine management and application orchestration. User applications, builds and Actions run in separate containers. The Runner is a trusted management service, not a shared process in which users execute arbitrary code.

Automaton maintains its own server with its own building blocks where possible: premade maintenance apps with Actions and schedules, rather than task-specific platform code. See [self-maintenance](self-maintenance.md); they are installed like any store app ([app-install-and-stores](app-install-and-stores.md)).

## Plans

Each plan is a file named after the plan (for example `self-maintenance.md`), with the same name as its parent issue and its value in the board's Plan field. Names carry no numbers; priority is the order of this table and the board's Priority field. This table lists open plans first, in priority order, then completed ones.

### Design here, work and status in GitHub

- **These files** hold the design: why, architecture, security rules and decisions. Change them in reviewed PRs.
- **GitHub issues** hold the work. Each plan in progress has a parent issue; each step is a sub-issue with full context and acceptance criteria. Native *blocked by* links give the order: an issue without open blockers can start. Labels `agent:ready`, `agent:blocked`, `agent:in-progress` and `agent:human-review` show who can pick it up.
- **Status** is the issue state and the [Automaton roadmap board](https://github.com/users/rzvdaniel/projects/1) (Status, Priority, Plan). A PR closes its issue and updates the step's one-line status in the plan file; do not keep detailed progress notes in these files.
- New work found while implementing becomes a new sub-issue of the plan's parent issue, not a paragraph here.
- Issue titles say what the work does, without plan or step numbers; *blocked by* links give the order.

| Plan | Status |
| --- | --- |
| [PostgreSQL](postgresql.md) | **Next** ([#584](https://github.com/rzvdaniel/automaton/issues/584)): move from MySQL before the public launch; .NET 10 follows on Npgsql |
| [.NET 10 LTS](dotnet-10.md) | **Next** ([#492](https://github.com/rzvdaniel/automaton/issues/492)): .NET 9 support ends 10 November 2026, after which it gets no security fixes. Touches every project, so it goes alone, before other code work |
| [Public launch](public-launch.md) | Planned ([#560](https://github.com/rzvdaniel/automaton/issues/560)): ready for visitors from articles: fresh-server install pass (after .NET 10), security policy, public pages, community setup, demo |
| [Self-maintenance](self-maintenance.md) | **In progress** ([#490](https://github.com/rzvdaniel/automaton/issues/490)): app secrets, shared vault, app permissions and the secret audit log done; next encrypted backup download (#484) and our nightly maintenance apps (#556) |
| [App install and shared stores](app-install-and-stores.md) | Planned ([#551](https://github.com/rzvdaniel/automaton/issues/551)): install any store app from Studio, from this or another server; deploy from stored source (#552) can start now |
| [Enhancements](enhancements.md) | Ongoing ([#495](https://github.com/rzvdaniel/automaton/issues/495)): independent improvements and bugs not part of another plan |
| [Self-hosted core](self-hosted-core.md) | Done (M1–M4). Two-user isolation and restart/replay checked on the test server: no cross-user access; #521 closed; the fixes are verified in 0.1.33 (#571, #572, #576, #581) or merged for the next release (#588, #577) |
| [App store](app-store.md) | Done (server 0.1.12). Follow-up ideas only |

## Working rules

Keep verification proportional to changed behavior. Run focused contract/unit checks for decision logic, Linux fixtures for filesystem/container behavior, and real-database checks when changing migrations, SQL predicates or concurrency. A build alone cannot establish those behaviors.

For runtime changes, follow the repository-required affected-project and full-solution builds and run the end-to-end acceptance relevant to the plan. Record commands, results and unavailable checks in the implementation PR, not in the roadmap. Mark a plan or step done only with its evidence (PR, release, validation) and remaining limitations. Production credentials, deployments and data changes remain separate operations.

## Deliberately out of scope

Public multi-tenant hosting, automatic cloud capacity purchasing, managed VM/microVM creation, Kubernetes, cross-region failover, distributed storage, a replacement message bus, a new workflow engine and a custom agent orchestrator. Private-only/offline and ARM installation are not the supported quickstart. Optional provider integrations can be considered after the self-hosted path is useful and simple to operate.
