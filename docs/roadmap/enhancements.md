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

**Done** (#873). App traffic keeps one path, the API runtime tunnel, so the single access gate and the Runner's
removal of `automaton_runtime_*` cookies stay as they are. Response bodies that stream (event streams, bodies with no
known length or larger than one message) are relayed chunk by chunk with an acknowledgement window, so memory stays
bounded with slow clients. WebSocket upgrades are relayed through the same tunnel. The Runner handles each tunnel
request on its own task and refuses more than 64 in flight per app; the API allows 20 open streams and WebSockets per
app and closes one that is idle for 300 seconds. The API logs `Tunnel latency` for every tunnel request (round trip,
time the app took, and the difference). Direct Traefik routing was dropped; revisit it only if those numbers demand it.

## Repair tests that fail on main

**Done** (#531, #532, closes #498). The `Tests` workflow runs every .NET test project and the Studio tests on
Linux for each pull request and on `main`. The 100 test methods that failed on `main`, mostly seeding Runners with
retired profiles, were deleted with the owner's agreement rather than repaired. Some covered paths that still exist
(action retries, secret redaction in logs, container resource limits); add new tests with current setup if those
areas change.
