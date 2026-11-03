# Enhancements

Improvements found while building and operating apps on Automaton. They are not
milestone prerequisites; the [roadmap index](README.md) lists all plans.
Each item states the problem, the current evidence, and the acceptance that
closes it. Work: parent issue [#495](https://github.com/rzvdaniel/automaton/issues/495) (runtime logs #496, real-time
connections #497; failing tests #498 done).

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

**Done** (#531, #532, closes #498). The `Tests` workflow runs every .NET test project and the Studio tests on
Linux for each pull request and on `main`. The 100 test methods that failed on `main`, mostly seeding Runners with
retired profiles, were deleted with the owner's agreement rather than repaired. Some covered paths that still exist
(action retries, secret redaction in logs, container resource limits); add new tests with current setup if those
areas change.
