# Initial analyzer priority baseline

The staged remediation and verification work is described in [Critical analyzer findings remediation plan](critical-analyzer-findings-remediation-plan.md).

Recorded against `336634f9` on 2026-09-23. This is a review aid for the standalone Sonar and .NET analyzers, not a SonarQube quality gate. Analyzer findings are potential defects; review the code before changing behavior.

## How the inventory was produced

Run `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal`. The command built successfully with 0 errors and 1,978 warning emissions. Deduplicating identical diagnostic lines in the captured build output yielded 1,971 findings. This count includes compiler warnings, .NET analyzers, Sonar rules, test projects, and dependent projects. Repeated builds or different tool versions can change the count; it is not a maintained debt target.

The standalone `dotnet build` output has diagnostic IDs and locations, but does not provide a ready-made ordering by Sonar's security, reliability, and maintainability impact. Use the following reviewed rule priorities. Do not treat the number of findings as a severity ranking.

| Priority | Rule and current count | Decision |
| --- | --- | --- |
| Review urgently | `S4830` certificate validation: 4 | Security rule with high impact. Keep as a warning until the existing uses are reviewed and fixed or narrowly justified. Do not erase this debt by suppressing the rule. |
| Build error | `S1764` identical expressions on both sides of an operator: 0 | Promote to error. A new finding indicates a likely logic defect. |
| Build error | `S2757` likely mistaken `=+`/`=-` assignment: 0 | Promote to error. A new finding indicates a likely assignment defect. |
| Review changed code | `S3776` cognitive complexity: 132; `CA1502` cyclomatic complexity: 102; `CA1506` coupling: 329 | Keep visible as warnings while historical debt is reduced. New or worsened findings in changed symbols require review under `AGENTS.md`. |
| Other warnings | Remaining Sonar, .NET, and compiler diagnostics | Review by rule meaning and affected behavior. Do not promote a whole category without checking historical findings and false positives. |

The `S4830` locations at this baseline are:

- `src/Automaton.Infrastructure/Email/EmailService.cs`: unconditional certificate-validation bypass during SMTP sends. This is the first location to review.
- `src/Automaton.Runner/Program.cs`: two development-mode certificate-validation bypasses. Verify the environment guard and whether the rule can be satisfied without changing local development behavior.
- `src/Automaton.Runner/Services/HubService.cs`: certificate-validation bypass in a development-mode connection path. Verify the guard and trust configuration.

The two promoted rules represent clear correctness defects, had no findings in the entire solution at baseline, and were confirmed to trigger in a standalone build using deliberate violations. They were not selected simply because their count was zero. `S2259` and `S2583` had zero findings, but simple deliberate violations did not trigger them in a standalone build; do not treat them as enforced by this package without further validation. The existing `S4830` findings have higher security impact and should be handled separately, with behavior tests and the relevant runtime/production guidance before that rule becomes a build error.

## Enforcement and next review

`.editorconfig` promotes only `S1764` and `S2757` to errors. Every C# project under `src/` inherits the standalone analyzer through `src/Directory.Build.targets`. A violating build fails in Debug and Release. CI must run a build that includes the affected project; an EditorConfig error does not protect code excluded from the CI path or a direct commit made without running CI.

After resolving the four certificate findings, promote `S4830` to error and run the full solution build. Reassess the remaining warning rules in small groups, using actual findings, rule impact, and false-positive rate. Keep a failing historical rule as a warning until its existing cases have a reviewed disposition; do not make all warnings errors.

References: [Sonar rule severity model](https://docs.sonarsource.com/sonarqube-cloud/standards/managing-rules/rules), [S4830 metadata](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S4830.json), [S1764 metadata](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S1764.json), [S2757 metadata](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S2757.json).
