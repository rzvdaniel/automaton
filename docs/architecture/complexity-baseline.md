# Complexity and coupling gate

The all-project analyzer gate builds every tracked C# project under `src/`, including tests and runtime fixtures. It records SARIF per project and checks `S3776` (cognitive complexity), `CA1502` (cyclomatic complexity), and `CA1506` (type/method coupling).

## Reviewed starting baseline

Recorded on 2026-09-23 from `main` commit `39ff68887f09bf3086130b039fe538b763417fc0`, using .NET SDK `9.0.318`, compiler `4.14.0`, and the pinned `SonarAnalyzer.CSharp` package `10.34.0.3385`. The scan covered all 22 tracked `.csproj` files, including four fixtures/projects outside `Automaton.All.sln`.

| Rule | Existing findings |
| --- | ---: |
| `S3776` cognitive complexity | 132 |
| `CA1502` cyclomatic complexity | 102 |
| `CA1506` coupling | 340 |
| **Total unique symbols/rules** | **574** |

These are unique SARIF findings. Normal solution build output repeats some project diagnostics when referenced projects are built by multiple consumers, so its raw warning count is not the baseline count.

The baseline lives in [`scripts/quality/complexity-baseline.json`](../../scripts/quality/complexity-baseline.json). Its identity uses the rule, project-relative file, source symbol, and normalized declaration signature; numeric scores are compared separately. The baseline records the analyzer configuration digest, SDK, and compiler. Missing reports, unsupported SARIF, duplicate identities, build failures, tool drift, and analyzer-configuration changes fail closed.

The largest method findings are an initial review queue, not an automatic refactoring order. Risk, test coverage, and recent change history still need to guide batch selection.

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

## Running the check

Run the strict no-regression gate locally or in CI:

```text
python scripts/quality/complexity_gate.py
```

The command uses the exact .NET SDK pinned in the baseline, builds all tracked C# projects, and writes ignored per-project reports under `.artifacts/quality/complexity/`. New findings and higher scores fail. A lower score is accepted while its symbol remains above the analyzer threshold; resolved symbols are reported as cleanup progress.

To list the current findings without applying the comparison, run:

```text
python scripts/quality/complexity_gate.py --report-only
```

`--require-zero` additionally fails while any historical finding remains. Use it after the backlog is cleared. `--create-baseline` exists only for an explicitly reviewed first baseline and refuses to overwrite one. Do not regenerate the baseline to accept debt, move findings, or bypass a regression.

## Cleanup target

The checked-in baseline is temporary migration state, not an accepted debt allowance. Refactor existing production and test symbols in small behavior-preserving batches until the no-regression gate reports zero findings. Then verify with `--require-zero`, promote the three diagnostics to build errors, and remove the historical baseline exception from the CI path. Do not reduce configured thresholds or create trivial helpers/interfaces simply to lower a score. Review complexity and coupling together with responsibility ownership, behavior coverage, and the total design.
