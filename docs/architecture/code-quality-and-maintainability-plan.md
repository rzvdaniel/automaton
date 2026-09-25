# Code quality and maintainability implementation plan

Status: Stage 0's all-project analyzer inventory and Stage 1's build-time analyzer enforcement are implemented on `codex/complexity-quality-gate`. `Automaton.All.sln` includes all 22 tracked C# projects, and `CA1502`, `CA1506`, and `S3776` are build errors. The Studio Server Azure pipeline builds and tests that solution on pushes and pull requests targeting `main`; it also publishes the API artifact. GitHub's branch-protection API rejected access because the private repository's current plan does not support that feature, so required-check enforcement remains unavailable.

Date: 2026-09-23. Repository inspection baseline: `3eb1c2cd`.

## Purpose and scope

Restore clear responsibility boundaries, reduce difficult control flow, and prevent new maintenance debt in both human-written and Codex-written code. Deliver this through executable checks, specific repository guidance, and small refactoring changes that preserve behavior.

This document records the staged implementation and remaining work. The status above identifies completed stages; implement later cleanup stages only when explicitly requested. Recording this plan does not authorize a repository-wide rewrite, production deployment, paid service adoption, or changes to external branch policies.

Start with the C# API, Runner, Worker, and shared infrastructure. Include Core, Messaging, Identity, and CLI in dependency and analyzer coverage as appropriate to their projects. Extend the same workflow to Studio and the website using their existing TypeScript and ESLint tooling. Do not impose Automaton implementation conventions on user-hosted application source.

Success means new changes have clear owners, bounded complexity, enforceable dependency boundaries, and behavioral coverage. A lower line count or larger number of classes is not sufficient evidence of improvement.

## Current evidence and gaps

The following observations come from repository inspection, not a completed static analysis audit:

| Observation | Evidence | Implication |
| --- | --- | --- |
| Large service files | `AppsService.cs`: 1,513 lines; `RunnerService.cs`: 1,493; `RunnerHostService.cs`: 1,684 | Candidates for responsibility review; file size alone does not prove excessive method complexity. |
| Broad dependency surface | `AppsService` has 11 constructor dependencies | Inspect whether query, mutation, authorization, storage, and runtime concerns have distinct owners. |
| Supporting types mixed with services | Public result records follow `RunnerService`; `StripeOptions` is declared in `StripeService.cs`; `AppsTunnelConfig` lives under `Services` | Establish ownership and placement conventions before moving files. |
| Existing dependency safeguards | `src/UnitTests/Automaton.Architecture.Tests/DistributedPlatformArchitectureTests.cs` checks project references | Extend the existing test project rather than creating another architecture framework by default. |
| CI and branch enforcement | `azure-pipelines-studio-server.yml` builds and tests `Automaton.All.sln` on pushes and pull requests targeting `main`; GitHub branch protection APIs are unavailable for this private repository on its current plan | Keep the all-project build/test status visible. Required checks need a repository policy mechanism supported by the current plan. |
| Analyzer configuration | Root `.editorconfig` promotes CA1502, CA1506, and S3776 to errors; `src/Directory.Build.targets` adds SonarAnalyzer.CSharp to the tracked projects | Review and clear findings before promoting more rules or tightening thresholds. |
| Frontend checks exist | Studio has type checking, ESLint, and Vitest; the Vue pipeline runs type checking, lint, and build | Reuse them and ensure quality checks do not modify source. The current `lint` script uses `--fix`. |

Actual method complexity, coupling, duplication, test adequacy, and change frequency remain to be measured. Candidate cleanup order must follow that evidence.

## Design policy to encode

### Responsibility and object design

- Application services coordinate a named use case. Business decisions belong to a cohesive domain object or policy with explicit inputs and outputs.
- Give business invariants and lifecycle transitions a clear owner. Do not scatter equivalent status assignments or eligibility rules across unrelated services.
- Keep I/O, transaction coordination, and external integration separate from pure decisions where that creates a useful, testable boundary.
- Preserve atomic database predicates and concurrency checks. A domain method cannot replace database coordination simply because it encapsulates a property assignment.
- Prefer composition. Introduce an interface for a meaningful boundary, alternate implementation, or test seam; do not require one for every class.
- Keep DTOs and transport messages data-oriented. Pure static functions are appropriate for stateless transformations. Avoid inheritance hierarchies or mutable domain objects introduced solely to appear more object-oriented.
- Extract a collaborator when it has a coherent responsibility and a small contract. A private method is sufficient when behavior belongs to the existing class.
- Do not game complexity limits by spreading one algorithm across meaningless helpers, local functions, partial classes, boolean flags, or a new generic context object.
- Preserve query translation, round trips, streaming, cancellation, exception semantics, disposal, and transaction boundaries when extracting methods or collaborators.

### Type ownership and placement

Use existing project boundaries. Decide the owning feature before selecting a folder.

| Type | Placement policy |
| --- | --- |
| Public API request/response DTO | Existing API model conventions, grouped by feature where useful; no service or infrastructure dependency |
| Configuration/options | Existing owning project's configuration area; preserve binding names and defaults |
| Persistence entity | Existing entity area; no mechanical conversion into a domain aggregate |
| Feature policy/value/result type | Next to the owning feature or in its explicit policy/model subfolder |
| Private implementation detail | Keep private or internal near its only consumer; a private nested type can be appropriate |
| Integration adapter | Owning infrastructure/host implementation boundary |
| Cross-project transport contract | Existing contract boundary, only when it actually crosses that boundary |
| General utility | Share only when multiple real consumers have the same semantics; use a precise name and minimal dependencies |

For the first pilot, use feature subfolders inside existing areas where practical. Do not introduce a new top-level `Features` tree or new assemblies until the pilot demonstrates a need. Preserve public namespaces during mechanical moves unless a separately reviewed change requires otherwise.

Do not create a global `Utilities` or `Models` dumping ground, promote every result type to Core, or create empty folders and interfaces for future use. Folder tidiness is secondary to ownership and dependency direction.

## Tooling and enforcement decision

Use existing .NET analyzers plus a pinned `SonarAnalyzer.CSharp` package for local feedback. Centralize package/configuration policy for every C# project under `src/` without replacing project-specific settings. Pin the analyzer version and metric thresholds in repository configuration.

Configure selected diagnostic severities through `.editorconfig`; configure analyzer-specific numeric parameters through the supported mechanism for the pinned analyzer version. Prove configuration with a deliberately violating sample. Do not assume an arbitrary EditorConfig key changes a Sonar threshold.

SonarQube Server or Cloud is optional for history, PR reporting, and centralized quality profiles. The initial gate must work without a hosted service or credentials. Before adopting a service, verify repository integration, current edition capabilities, licensing, and source-data requirements. Avoid duplicate local and scanner analyzer configuration that yields inconsistent results.

### Initial thresholds

These values were calibrated against the initial inventory. Stage 1 promotes the three measured complexity/coupling diagnostics to build errors so the cleanup backlog is visible in normal builds.

| Signal | Initial value | Treatment |
| --- | --- | --- |
| Cognitive complexity | Greater than 15 per method | Build error until historical violations are resolved |
| Cyclomatic complexity | Greater than 15 per method | Build error until historical violations are resolved |
| Type/method coupling | Existing analyzer threshold | Build error until historical violations are resolved |
| Control-flow nesting | More than 3 levels | Review signal; choose a supported analyzer rather than count braces |
| Method size | More than about 60 logical lines | Review signal; document the measurement convention |
| Constructor dependencies | More than 7 injected dependencies | Responsibility review, not a demand for a dependency-bag object |
| File size | More than about 500 lines | Triage signal, not a merge gate |
| Forbidden project/type dependency | Any new violation | Block |
| Duplication | Baseline and inspect changed regions | Advisory until measurement and exclusions are reliable |
| Selected correctness diagnostics | Explicit rule list | Build errors when findings are clear and reviewed |

Use one authoritative diagnostic for each blocking concept. For example, avoid simultaneously failing multiple analyzers for the same complexity issue. Exclude generated code using explicit, reviewed paths or generated-code metadata. Apply appropriate separate settings to tests and fixtures rather than excluding all tests from correctness analysis.

### Build-time error policy

1. Keep the analyzer package and thresholds centrally configured so every project under `src/` runs the same rules during ordinary builds.
2. Treat `CA1502`, `CA1506`, and `S3776` as compiler build errors. Do not grandfather existing findings behind a baseline or suppress them just to restore a green build.
3. Build the affected project during focused work so diagnostics identify the relevant symbols. Build `Automaton.All.sln` before handoff and in CI to cover all projects, tests, and fixtures.
4. Fix the historical errors in reviewable, behavior-preserving batches. Use the analyzer's diagnostic IDs, locations, and measured scores to guide each batch; do not approximate scores or refactor solely to lower numbers.
5. If an analyzer finding is demonstrably false-positive or incompatible with required behavior, document the exact rule/symbol and removal condition and request review for a narrow exception.

## Delivery stages

### Stage 0 — Inventory and reproducible baseline

Deliverables:

- A short baseline report under `docs/architecture/`, with the measured commit, commands, tool versions, scope, and known gaps.
- A ranked backlog of approximately ten hotspots: symbol, owning feature, complexity, dependencies, recent churn, behavioral coverage, risk, and proposed responsibility split.
- A proposed pilot and a documented reason for selecting it.
- An inventory of solution membership, existing tests, CI entry points, and relevant exclusions.

Work:

1. Capture a clean or explicitly described working-tree state; preserve unrelated changes.
2. Run the all-project build with analyzers in warning mode to capture the initial inventory. Keep the raw build log out of source control and retain the compact reviewed counts and hotspot queue in the architecture documentation.
3. Rank complexity together with frequency of change and behavioral risk. Do not rank solely by file length.
4. Inspect the top candidates manually for mixed responsibilities, duplicated decisions, and public contracts.
5. Confirm existing tests and identify missing characterization cases before selecting the first refactor.

Candidate areas include `AppsService`, `RunnerService`, `RunnerHostService`, and hosted-runner lifecycle reconciliation. They are candidates, not a pre-approved rewrite list. `HostedRunnerLifecycleReconcilerTests` and deployment-target tests provide potential starting coverage, but their existence does not establish completeness.

Exit criteria: another developer can reproduce the inventory; actual method scores replace guesses; thresholds, exclusions, and the pilot have a documented rationale. No production behavior changes in this stage.

### Stage 1 — Build-time analyzer errors across all projects

Depends on Stage 0.

Deliverables: central analyzer settings and references, an all-project solution, direct compiler diagnostics in local builds, and the same build in CI.

Work:

1. Add central configuration, taking care that `Directory.Build.props` settings may affect every descendant project and fixture. Preserve nullable settings and target frameworks; keep unrelated warning severities unchanged.
2. Pin analysis tooling and define the selected diagnostics as build errors in `.editorconfig`.
3. Ensure `Automaton.All.sln` includes every tracked C# project, including tests and fixtures.
4. Build the affected project locally and the all-project solution in CI. Let standard MSBuild analyzer diagnostics fail the build directly.
5. Always run the architecture test project for backend/boundary changes. Run behavioral tests appropriate to the selected scope; support an explicit full verification mode for CI.
6. Add separate frontend `lint:check` scripts without `--fix`. Retain an explicit fixing command for developers. Use existing type checks and tests.
7. Document the exact installed commands and failure output. Avoid host-specific paths, credentials, or implicit downloads of unpinned tools.

The same `dotnet build` commands must run on developer machines and Linux CI. No custom script runtime or remote Sonar service is required for analyzer diagnostics.

Exit criteria: deliberate complexity/coupling violations fail a build; all tracked projects are in the all-project solution; local and CI commands display standard analyzer errors; historical debt is being removed in scoped batches.

### Stage 2 — Required CI and architecture coverage

Depends on Stage 1.

Deliverables: CI all-project build, explicit architecture-test execution, and documented required-check configuration.

Work:

1. Wire the all-project solution build into the relevant Azure pipeline entry points. Determine coverage across API, Runner, Worker, CLI, Studio, and shared projects; do not assume the server solution covers everything.
2. Invoke `src/UnitTests/Automaton.Architecture.Tests/Automaton.Architecture.Tests.csproj` explicitly, avoiding dependence on incidental solution membership.
3. Align restore/build/test configuration so `--no-build` never consumes a mismatched output. Keep unrelated pipeline modernization out of scope.
4. Include shared configuration, scripts, and architecture tests in path triggers. Establish actual PR behavior for the repository provider and target branch; YAML alone is not proof of branch-policy enforcement.
5. Keep the analyzer build output visible in pipeline logs so developers and coding agents can inspect diagnostic IDs and locations.
6. Make the check required through the repository's actual branch protection/build-validation mechanism when authorized. Record any external configuration still outstanding.
7. Verify a deliberate violation fails the build. As cleanup reaches zero for the selected rules, verify the all-project build passes.

Extend type-level architecture tests only for agreed seams. Use reflection, compiler symbols, or an established architecture library if it provides a clear benefit. Keep XML project-reference tests where they already express the intended rule well. Avoid source substring checks and blanket rules inferred only from class suffixes.

Exit criteria: all relevant change paths trigger the checks; architecture tests demonstrably execute; required-check enforcement is verified or clearly recorded as pending. Do not mark enforcement complete merely because a pipeline YAML file exists.

### Stage 3 — Codex engineering contract and refactoring workflow

Depends on Stage 1; can proceed while Stage 2 integration is completed.

Deliverables: a concise `AGENTS.md` update and one focused repository skill, proposed as `.agents/skills/maintainability/SKILL.md`, explicitly added to the repository skill contract.

This stage names the new skill work intentionally. Follow the skill-creator guidance when implementing it. Do not expand unrelated developer-local skills or add agent roles, loops, or orchestration machinery.

Put durable rules and the build commands in `AGENTS.md`. Put the detailed refactoring procedure and examples in the skill. Avoid duplicating the entire plan in both places. Add the pilot's verified before/after example after Stage 4.

Proposed contract:

> Before extending a service, identify the responsibility being added and its owner. Keep business decisions cohesive and orchestration readable. Follow the owning feature's placement conventions. Preserve observable behavior during cleanup. Run the affected project build, all-project analyzer build, and relevant behavioral tests. Do not weaken rules or introduce suppressions merely to finish a task. Explain any justified exception explicitly.

The skill should require a short responsibility map for substantial refactors, characterization coverage, small steps, and a final diff review. Routine small edits should not require a design essay.

Review questions:

- Does each new type have a clear owner and reason to exist?
- Are business invariants still duplicated or scattered despite lower method scores?
- Did extraction move complexity to another large service or generic helper?
- Did a new abstraction reduce coupling, or only add indirection?
- Are DTOs, options, provider details, and persistence concerns at the intended boundaries?
- Are the checks unchanged and their results reproducible?

Exit criteria: guidance names actual commands, gives concrete examples and exceptions, and works within the existing Symphony/repository workflow. A representative future change can follow it without relying on conversation history.

### Stage 4 — Pilot refactor and convention validation

Depends on Stages 0–3; required enforcement should be active before broad cleanup.

Choose one bounded use case within a hotspot. Do not attempt all of `AppsService` or `RunnerHostService` at once. If the chosen seam lacks reliable behavioral coverage, first add that coverage or choose a smaller seam.

Work:

1. Document the current public inputs/outputs, side effects, failure behavior, state ownership, and dependencies.
2. Add missing characterization tests around those behaviors. Avoid tests that merely assert the new private method structure.
3. Draw a short responsibility map: orchestration, decisions, persistence, integration, and transport models. Choose the smallest useful split.
4. Move public supporting types to appropriate files without changing public contracts. Separate mechanical moves from substantive refactoring where practical.
5. Extract cohesive policies or collaborators; preserve cancellation, DI lifetimes, event ordering, query semantics, and transaction/concurrency boundaries.
6. Run the smallest relevant project build after each change, then the all-project analyzer build and affected integration/acceptance checks.
7. Record before/after complexity and dependency evidence together with a readable design explanation. Check total responsibilities and call flow, not only the entry method's score.
8. Document the resulting convention and a compact example in the maintainability skill.

Exit criteria: behavior and public contracts remain stable; the targeted complexity/responsibility problem is reduced; no replacement god-service or helper sprawl appears; the affected project reports fewer relevant errors without new suppressions; review confirms the design is easier to follow.

### Stage 5 — Incremental cleanup batches

Depends on successful Stage 4. Re-rank the backlog after the pilot.

Suggested sequence:

1. Supporting DTO/options placement in the selected features, preserving namespace and serialization compatibility.
2. Repeated pure decisions and validation with clear policy ownership.
3. Query/use-case separation inside broad application services.
4. Lifecycle and deployment orchestration, only with adequate retry/concurrency coverage.
5. Runner process/container integration boundaries, retaining generic-runtime guarantees.
6. Studio composables, service modules, and component responsibility where analysis shows equivalent problems; apply Vue and Studio design skills for those changes.

Each PR should address one coherent responsibility boundary and include its verification evidence. Keep feature additions and broad formatting changes separate. Do not move unrelated models just because a folder is being edited.

Exit criteria per batch: defined behavior preserved, selected debt reduced, no new analyzer errors introduced, appropriate regression tests pass, and remaining work is recorded in the backlog.

### Stage 6 — Routine maintenance

Depends on active enforcement and at least one completed cleanup batch.

- Reserve a small explicit cleanup allocation per development cycle; initially one bounded hotspot or roughly 10% of engineering capacity, adjusted after observing actual cost.
- When changing a violating method, reduce its analyzer errors and clarify nearby ownership within the task's scope. Do not trigger a whole-service rewrite for a small bug fix.
- Review remaining hotspots, new exceptions, and analyzer noise monthly. This cadence is a team practice, not an automation created by this plan.
- Upgrade analyzer versions in dedicated changes with rule/configuration review; avoid surprise diagnostic changes during feature work.
- Promote advisory rules only after they produce actionable findings with acceptable noise.
- Track unresolved blocking debt, complexity of the highest-risk methods, new exceptions, and quality-check duration. Avoid class-count or line-count targets.

Exit criteria: a maintainer can run and interpret the checks, reviewers apply the design contract, exceptions are reviewed, and debt decreases without recurring broad cleanup releases.

## Refactoring verification and invariants

Select tests based on actual behavior affected. The following matrix is a coverage guide, not a requirement to invent every test for every file move.

| Changed concern | Required evidence where applicable |
| --- | --- |
| DTO/options/file placement | Compile all consumers; preserve namespaces, serializer names, binding keys, validation, defaults, and DI registration |
| Domain policy | Representative allowed/denied cases, boundaries, missing data, and stable error semantics |
| EF query extraction | Equivalent filtering, authorization, ordering, pagination, translation, and database round-trip behavior |
| Lifecycle/state transitions | Legal/illegal transitions, duplicate delivery, stale ownership, optimistic concurrency, crash/retry recovery, cancellation |
| Database/message coordination | Durable operation identity, atomic claim/update behavior, transaction/event consistency, failure paths; use relational evidence where in-memory tests cannot establish correctness |
| Runner/runtime integration | Process and container cleanup, timeout/disposal behavior, source/live separation, persistence boundaries, provider independence |
| Public serving/archive/fork | No newly exposed private files; portable source and attribution preserved; runtime output and runner assignment excluded as required |
| Studio UI | Type checks, focused behavioral tests, build, and the Studio design visual checklist |

Preserve the distributed-platform checklist: multiple API/worker replicas, duplicate messages, process death, shared authoritative state, provider independence, storage affinity, and stable application identity. Do not introduce locks, new storage infrastructure, deployment changes, or a state-machine framework simply to simplify a method.

For cleanup with no administrator-visible effect, explicitly state that Admin docs do not need changing. If behavior, permissions, statuses, routes, or workflows change intentionally, separate and document that change under the admin-docs skill. This plan itself has no administrator-visible effect.

Existing verification commands that can be used today include:

```text
dotnet test src/UnitTests/Automaton.Architecture.Tests/Automaton.Architecture.Tests.csproj -c Release
dotnet test src/UnitTests/Automaton.Api.Tests/Automaton.Api.Tests.csproj -c Release --filter FullyQualifiedName~HostedRunnerLifecycleReconcilerTests
git diff --check
```

These examples are not a complete verification suite. The all-project analyzer build is part of Stage 1; frontend checks remain separate work in the relevant frontend pipelines.

## Change boundaries, handoff, and recovery

- Use the existing checkout and preserve unrelated working-tree changes. Follow the branch instruction for each implementation task.
- Every stage handoff lists implemented files, exact checks and outcomes, unavailable checks, analyzer errors resolved or remaining, and acceptance criteria.
- Keep configuration/gate changes and application refactors independently reviewable. Revert a faulty refactor without removing the safeguards.
- If a diagnostic is defective, isolate the smallest justified exception with a removal condition. Do not disable the all-project build or suppress whole rule categories.
- Do not change production state, deploy, or publish Runner images as part of cleanup unless a separate request authorizes it and the production skill is followed.
- Do not claim behavior preservation from compilation alone, or claim CI enforcement from a successful local run alone.

## Completion checklist

- [ ] Stage 0: measured baseline and prioritized backlog recorded.
- [ ] Stage 1: reproducible local all-project analyzer build verified; selected diagnostics fail builds.
- [ ] Stage 2: all relevant CI paths and required-check behavior verified.
- [ ] Stage 3: concise Codex contract and focused maintainability skill installed in the repository contract.
- [ ] Stage 4: one pilot demonstrates simpler ownership and stable behavior without new exemptions.
- [ ] Stage 5: prioritized cleanup batches completed or explicitly scheduled with remaining debt recorded.
- [ ] Stage 6: maintenance ownership, review cadence, and exception policy established.

## References

- [Repository contract](../../AGENTS.md)
- [Distributed platform architecture skill](../../.agents/skills/distributed-platform-architecture/SKILL.md)
- [Generic runtime skill](../../.agents/skills/generic-runtime/SKILL.md)
- [Admin documentation skill](../../.agents/skills/admin-docs/SKILL.md)
- [Existing architecture guardrails](../../src/UnitTests/Automaton.Architecture.Tests/DistributedPlatformArchitectureTests.cs)
- [Sonar .NET analyzer configuration](https://github.com/SonarSource/sonar-dotnet#configuring-rules)
- [Sonar cognitive complexity rule metadata](https://github.com/SonarSource/sonar-dotnet/blob/master/analyzers/rspec/cs/S3776.json)
- [.NET analyzer configuration](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files)
- [.NET cyclomatic complexity rule CA1502](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1502)
- [Sonar new-code quality approach](https://docs.sonarsource.com/sonarqube-server/2025.5/user-guide/about-new-code)
- [Codex repository guidance and verification practices](https://learn.chatgpt.com/guides/best-practices)

External references explain the tools; the adopted repository configuration and tests remain authoritative. Recheck version-specific parameters and service capabilities during implementation.
