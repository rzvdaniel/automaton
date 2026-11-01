# 03 Enhancements

Improvements found while building and operating apps on Automaton. They are not
milestone prerequisites; the [roadmap index](README.md) lists all plans.
Each item states the problem, the current evidence, and the acceptance that
closes it. Work: parent issue [#495](https://github.com/rzvdaniel/automaton/issues/495) (runtime logs #496, real-time
connections #497, failing tests #498).

## Keep runtime logs readable after the fact

**Problem.** Studio's **Logs** view shows only output received while the page is
open ("not persisted", up to 100 recent entries). When a runtime fails or
restarts, its startup output cannot be read later. `automaton logs` only opens
Studio, so the terminal offers no alternative.

**Evidence.** App containers already keep rotated Docker logs on the Runner
(`HostContainerExecutionService` sets `max-size` and `max-file`), so recent
output exists but is not exposed.

**Direction.** Let Studio and the CLI request a bounded tail of an environment's
current container log from its Runner, shown next to the live stream. Consider
`automaton logs --tail <lines>` printing to the terminal.

**Acceptance.** Without having had the page open, an app owner can read the
current runtime's recent output, including startup lines, from Studio and the
CLI. Access follows existing app authorization, and the returned size is
bounded.

## Support real-time connections for hosted apps

**Problem.** Private and published app traffic passes through the API runtime
tunnel, which drops the `Upgrade` header and buffers each response
(`RuntimeTunnelController`, `RunnerRuntimeGatewayProxy`,
`RuntimeTunnelRequestSender`). WebSockets and Server-Sent Events cannot work, so
live apps must poll. The app contract documents this limitation.

**Direction.** First stream response bodies end to end (API, Runner tunnel,
container), which enables Server-Sent Events. Then evaluate forwarding WebSocket
upgrades. Preserve per-app routing, authentication, request limits, and tunnel
backpressure, and bound concurrent long-lived connections per app.

**Acceptance.** A deployed sample app streams Server-Sent Events and holds a
WebSocket session through both its private and published URLs. Tunnel memory
stays bounded with slow clients, and existing request/response apps behave as
before.

## Repair tests that fail on main

**Problem.** These tests fail on `main` (current counts in #498):

- CLI: `TargetCommand_ExposesRequiredManagementCommands` (expects removed
  `approve`/`revoke` subcommands), `Format_ShowsLiveRoleDestinationAndStatus`
  (expects "Automaton Hosted"), and
  `DeployAsync_WhenRunnerHostLacksInfrastructureRunner_DoesNotBlamePlan`.
- API: 82 of 794 tests. Most seed runners without an application profile and
  now throw `Unknown application Runner profile ''` from
  `RunnerApplicationProfiles.Normalize`, since the legacy profiles were
  removed (runner assignment, deployment targets, durable actions, machine
  management). `SeedCatalog_ExplicitlyRegistersEveryEmbeddedSeed` also expects
  seven registered guidance seeds; the catalog has six.
- Studio: `AppRuntimeLogs.test.ts` (multiline runtime output), and
  `public-app-url.test.ts` (private link host), which fails intermittently.

**Acceptance.** Each test either matches the intended current behavior or is
removed with its obsolete feature, and the suites pass on `main`.
