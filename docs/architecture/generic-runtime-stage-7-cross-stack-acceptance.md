# Generic runtime Stage 7: cross-stack acceptance

Stage 7 proves that the generic deployment path works for materially different
application stacks. The acceptance fixtures are deliberately ordinary HTTP
applications; none uses the deferred Automaton controller/FastAPI bridge.

## Acceptance matrix

| Fixture | Runner build command | Build output | Runtime command | Health | Probe |
| --- | --- | --- | --- | --- | --- |
| ASP.NET Core | `dotnet publish Acceptance.csproj -c Release -o out` | `out/` | `dotnet out/Acceptance.dll` | `GET /health` | `/hello` |
| Node.js | `node build.mjs` | `out/` | `node out/server.mjs` | `GET /health` | `/hello` |
| Python | `python build.py` | `out/` | `python out/server.py` | `GET /health` | `/hello` |

Every fixture is packaged as source plus one `ApplicationContract`. The Runner
performs the build once, selects the declared post-build output, stages it in a
candidate artifact root, starts the declared runtime command, probes the
declared health endpoint, promotes the ready candidate, and exposes it through
the same runtime gateway. The response also verifies the generic persistent
data directory injection.

Runtime command paths are relative to the artifact root. Therefore the examples
use `out/Acceptance.dll`, `out/server.mjs`, and `out/server.py` as arguments,
while the runtime working directory remains `.`. This matches the Stage 2
resolution rule and the Stage 3 artifact selector.

## Lifecycle coverage

The acceptance tests cover:

- one real build/runtime/readiness/gateway path for ASP.NET Core, Node.js, and
  Python;
- a Node.js replacement where the v1 runtime remains routed until v2 health
  succeeds, then v2 is promoted and routed;
- a failed Node.js replacement where the v1 process, artifact, metadata, and
  gateway response remain healthy;
- post-build files being present in the runtime artifact root; and
- a structural guard that the canonical deployment contract has no technology
  selector such as language, framework, or runtime type.

The fixture tests use free requested ports and a short temporary workspace root
so the acceptance suite is independent of unrelated local listeners and stays
within Windows path-length limits. These are test-environment concerns, not
runtime selection rules.

## Architectural boundary

The production pipeline contains no branch for ASP.NET Core, Node.js, Python,
or any other technology:

```text
source + ApplicationContract
        -> Runner build executor
        -> selected runtime artifact
        -> opaque runtime command
        -> TCP/HTTP readiness
        -> target runtime promotion
        -> generic gateway and route
```

The fixture source owns its server and build setup. The contract carries only
structured commands, paths, environment, port, health, and storage. No
technology name, filename convention, package manifest, or template identity
changes Runner behavior. The controller/action/FastAPI compatibility path,
Studio redesign, and the remaining legacy-coupling removal remain deferred to
later stages.

Static-file fallback is not part of this acceptance. An HTTP/public application
must declare `runtime.command`; a finite-work or Actions-only application may
omit it because it has no public HTTP endpoint.

## Future-stack criterion

Adding a genuinely different stack, such as Ruby, should require only a fixture
server/build setup and a matching generic contract. It must not require a
Runner, API, gateway, route, or process-supervision change. This is the
acceptance criterion for keeping the runtime boundary stack-neutral.
