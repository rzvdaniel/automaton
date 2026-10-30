# Initial analyzer priority baseline

The critical security/lifecycle remediation is described in [Critical analyzer findings remediation plan](critical-analyzer-findings-remediation-plan.md). This is a historical inventory from before `SonarAnalyzer.CSharp` was removed. Current .NET analyzer severities are defined in the root `.editorconfig`.

Recorded against `336634f9` on 2026-09-23. The inventory included the standalone Sonar and .NET analyzers; the Sonar package is no longer part of normal builds. Findings are potential defects; review code before changing behavior.

## How the inventory was produced

Run `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal`. The command built successfully with 0 errors and 1,978 warning emissions. Deduplicating identical diagnostic lines in the captured build output yielded 1,971 findings. This historical count includes compiler warnings, .NET analyzers, Sonar rules, test projects, and dependent projects. It is not a maintained debt target.

The standalone `dotnet build` output has diagnostic IDs and locations, but does not provide a ready-made ordering by security, reliability, and maintainability impact. The following table records historical priorities, not active Sonar build gates. Do not treat the number of findings as a severity ranking.

| Priority | Rule and current count | Decision |
| --- | --- | --- |
| Resolved and enforced | `S4830` certificate validation: 4 at inventory | The initial findings were remediated in PR #302; `S4830` is a build error. Do not erase future findings by suppressing the rule. |
| Historical Sonar gate | `S1764` identical expressions on both sides of an operator: 0 | Was promoted to an error; no longer runs in normal builds. |
| Historical Sonar gate | `S2757` likely mistaken `=+`/`=-` assignment: 0 | Was promoted to an error; no longer runs in normal builds. |
| Historical findings | `S3776` cognitive complexity: 132; `CA1502` cyclomatic complexity: 102; `CA1506` coupling: 329 | Only `CA1502` remains configured as a build error. The Sonar diagnostics are retained here as historical inventory data. |
| Other warnings | Remaining Sonar, .NET, and compiler diagnostics | Review by rule meaning and affected behavior. Do not promote a whole category without checking historical findings and false positives. |

The `S4830` locations at this baseline are:

- `src/Automaton.Infrastructure/Email/EmailService.cs`: unconditional certificate-validation bypass during SMTP sends. This is the first location to review.
- `src/Automaton.Runner/Program.cs`: two development-mode certificate-validation bypasses. Verify the environment guard and whether the rule can be satisfied without changing local development behavior.
- `src/Automaton.Runner/Services/HubService.cs`: certificate-validation bypass in a development-mode connection path. Verify the guard and trust configuration.

`S1764` and `S2757` represent clear correctness defects, had no findings in the initial inventory, and were confirmed to trigger in a standalone build using deliberate violations. They were not selected simply because their count was zero. `S2259` and `S2583` had zero findings, but simple deliberate violations did not trigger them in a standalone build; do not treat them as enforced by this package without further validation. The initial `S4830` findings were handled separately in PR #302 with behavior review and tests before the rule became a build error.

## Enforcement and next review

The historical root `.editorconfig` promoted several Sonar rules and `CA1502` to errors. The Sonar package and its rule entries have since been removed; `CA1502` remains configured. `Automaton.All.sln` includes all 22 tracked C# projects, including tests and fixtures. Other active .NET rules are configured in the root `.editorconfig`.

References: [Sonar rule severity model](https://docs.sonarsource.com/sonarqube-cloud/standards/managing-rules/rules), [S4830 metadata](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S4830.json), [S1764 metadata](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S1764.json), [S2757 metadata](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S2757.json).
