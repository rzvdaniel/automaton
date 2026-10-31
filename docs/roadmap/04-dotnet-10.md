# .NET 10 LTS

Updated: 31 October 2026. Status: postponed in favor of [05 self-maintenance](05-self-maintenance.md) (31 October 2026). Deadline: .NET 9 support ends on 10 November 2026 ([support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)); after that date it receives no security fixes.

## Scope

- All 22 projects target `net9.0`: retarget to `net10.0` (API, Worker, Runner, CLI, shared libraries, tests and fixtures).
- Docker images use `mcr.microsoft.com/dotnet/sdk:9.0` and `aspnet:9.0`: move to the 10.0 images (server, Worker, Runner).
- CI and release workflows: install the .NET 10 SDK.
- NuGet dependencies: update packages pinned to 9.x (EF Core, ASP.NET Core, Microsoft.Extensions.*) and the MySQL EF provider to versions that support .NET 10.
- CLI: `dotnet tool install -g Automaton.Cli` then requires the .NET 10 runtime. Update the stated requirement in the README, CLI guide and website docs (currently ".NET 9 SDK or later").

## Acceptance

- `Automaton.All.sln` builds with no new analyzer errors; affected tests show no new failures against the current baseline.
- A server release, a Runner image and a CLI package built on .NET 10 are published.
- The test server updates to that release with `update.sh`; Runners upgrade with **Runners → Upgrade runner**; a fresh CLI install deploys and publishes an app; a nightly backup and restore still work.
- Production is updated the same way, with public URLs checked before and after.
