# Complexity and coupling cleanup backlog

The inventory below is historical and includes findings from the now-removed Sonar analyzer. Current C# builds use the .NET analyzers; `CA1502` (cyclomatic complexity) is configured as an error. `S3776` and `CA1506` are no longer build analyzers.

## Initial inventory

The initial inventory was recorded on 2026-09-23 from `main` commit `39ff68887f09bf3086130b039fe538b763417fc0`, using .NET SDK `9.0.318`, compiler `4.14.0`, and `SonarAnalyzer.CSharp` `10.34.0.3385`. It covered all 22 tracked C# projects under `src/`, including test projects and fixtures. Since that scan, the Runner runtime fixture cleanup resolved three findings; the remaining historical work is being handled in reviewable batches.

| Rule | Initial findings |
| --- | ---: |
| `S3776` cognitive complexity | 132 |
| `CA1502` cyclomatic complexity | 102 |
| `CA1506` coupling | 340 |
| **Total** | **574** |

These counts describe the initial inventory. MSBuild can emit the same diagnostic more than once when a project is built through multiple consumers, so build-log line counts are not unique finding counts.

The highest initial method scores are a review queue, not an automatic refactoring order. Risk, recent change history, ownership, and behavioral test coverage should guide each batch.

| Candidate | Cognitive | Cyclomatic | Method coupling |
| --- | ---: | ---: | ---: |
| `OpenAiCompatibleProvider.CallChatCompletionsAsync` | 83 | 41 | 56 |
| `HostedRunnerStorageReconciliationService.ReconcileAsync` | 65 | 60 | 54 |
| `HostContainerRelayService.ExecuteAsync` | 63 | 74 | 51 |
| `GoogleProvider.CallGeminiAsync` | 61 | 31 | 48 |
| `DeployCommand.Build` | 58 | 27 | 50 |
| `PythonEnvironmentConfigurator.TryResolveAndSetPythonDll` | 57 | 32 | — |
| `OpenAiCompatibleProvider.StreamOnceAsync` | 55 | 27 | 38 |
| `LogoutCommand.Build` | 54 | 24 | — |
| `RunnersCommand.Build` | 50 | 28 | 41 |
| `OpenAiProvider.CallResponsesAsync` | 49 | 30 | 45 |

## Run analyzers locally and in CI

For focused work, build the project being changed so its analyzer diagnostics are easy to inspect:

```text
dotnet build src/Automaton.Runner/Automaton.Runner.csproj -c Debug --no-incremental -v minimal
```

Before handoff, build the all-project solution:

```text
dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal
```

`Automaton.All.sln` contains all 22 tracked C# projects, including test projects and fixtures. The Studio Server Azure pipeline runs the same all-project build in Release. It currently fails on historical findings until cleanup resolves them. This is intentional: builds should show the analyzer diagnostics directly to developers and coding agents, and each cleanup batch should remove errors without adding suppressions or weakening thresholds.

The all-project solution and project-level package reference keep analysis available without an online Sonar service. Analyzer versions are pinned in `src/Directory.Build.targets`; metric thresholds are in `src/CodeMetricsConfig.txt`; rule severity is in the root `.editorconfig`.

## Cleanup policy

Refactor production and test symbols in small behavior-preserving batches. Do not reduce thresholds or split an algorithm into trivial helpers just to lower a score. Review complexity and coupling together with responsibility ownership, behavior coverage, and the total design. Run affected tests for behavior changes and record analyzer errors resolved by the batch.
