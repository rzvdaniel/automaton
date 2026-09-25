# Initial analyzer priority baseline

The critical security/lifecycle remediation is described in [Critical analyzer findings remediation plan](critical-analyzer-findings-remediation-plan.md). This page records an initial inventory; current severities are defined in the root `.editorconfig`.

Recorded against `336634f9` on 2026-09-23. This is a review aid for the standalone Sonar and .NET analyzers, not a SonarQube quality gate. Analyzer findings are potential defects; review the code before changing behavior.

## How the inventory was produced

Run `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal`. The command built successfully with 0 errors and 1,978 warning emissions. Deduplicating identical diagnostic lines in the captured build output yielded 1,971 findings. This count includes compiler warnings, .NET analyzers, Sonar rules, test projects, and dependent projects. Repeated builds or different tool versions can change the count; it is not a maintained debt target.

The standalone `dotnet build` output has diagnostic IDs and locations, but does not provide a ready-made ordering by Sonar's security, reliability, and maintainability impact. Use the following reviewed rule priorities. Do not treat the number of findings as a severity ranking.

| Priority | Rule and current count | Decision |
| --- | --- | --- |
| Resolved and enforced | `S4830` certificate validation: 4 at inventory | The initial findings were remediated in PR #302; `S4830` is a build error. Do not erase future findings by suppressing the rule. |
| Build error | `S1764` identical expressions on both sides of an operator: 0 | Promote to error. A new finding indicates a likely logic defect. |
| Build error | `S2757` likely mistaken `=+`/`=-` assignment: 0 | Promote to error. A new finding indicates a likely assignment defect. |
| Build errors | `S3776` cognitive complexity: 132; `CA1502` cyclomatic complexity: 102; `CA1506` coupling: 329 in this inventory | These diagnostics are build errors now. The historical findings intentionally keep the all-project build failing while cleanup proceeds. |
| Other warnings | Remaining Sonar, .NET, and compiler diagnostics | Review by rule meaning and affected behavior. Do not promote a whole category without checking historical findings and false positives. |

The `S4830` locations at this baseline are:

- `src/Automaton.Infrastructure/Email/EmailService.cs`: unconditional certificate-validation bypass during SMTP sends. This is the first location to review.
- `src/Automaton.Runner/Program.cs`: two development-mode certificate-validation bypasses. Verify the environment guard and whether the rule can be satisfied without changing local development behavior.
- `src/Automaton.Runner/Services/HubService.cs`: certificate-validation bypass in a development-mode connection path. Verify the guard and trust configuration.

`S1764` and `S2757` represent clear correctness defects, had no findings in the initial inventory, and were confirmed to trigger in a standalone build using deliberate violations. They were not selected simply because their count was zero. `S2259` and `S2583` had zero findings, but simple deliberate violations did not trigger them in a standalone build; do not treat them as enforced by this package without further validation. The initial `S4830` findings were handled separately in PR #302 with behavior review and tests before the rule became a build error.

## Enforcement and next review

The root `.editorconfig` promotes `S1764`, `S2757`, `S4830`, `S5445`, `S4502`, `S2930`, `S2190`, `CA1502`, `CA1506`, and `S3776` to errors. Every C# project under `src/` inherits the analyzer package through `src/Directory.Build.targets`. `Automaton.All.sln` includes all 22 tracked C# projects, including tests and fixtures. The Studio Server CI pipeline builds this solution on pushes to `main` and pull requests targeting `main`; the same solution can be built locally. The complexity and coupling backlog intentionally keeps this build red until it is resolved. Other rules remain warnings unless separately reviewed and promoted.

References: [Sonar rule severity model](https://docs.sonarsource.com/sonarqube-cloud/standards/managing-rules/rules), [S4830 metadata](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S4830.json), [S1764 metadata](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S1764.json), [S2757 metadata](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S2757.json).
