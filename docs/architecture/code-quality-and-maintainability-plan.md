# Code quality and maintainability implementation plan

Status: proposed; implementation has not started.

Date: 2026-09-23. Repository inspection baseline: `3eb1c2cd`.

## Purpose and scope

Restore clear responsibility boundaries, reduce difficult control flow, and prevent new maintenance debt in both human-written and Codex-written code. Deliver this through executable checks, specific repository guidance, and small refactoring changes that preserve behavior.

This document is an implementation plan, not a statement that its rules or commands are already active. Implement only the stage named by a subsequent task. Recording this plan does not authorize a repository-wide rewrite, production deployment, paid service adoption, or changes to external branch policies.

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
| Potential CI coverage gap | Architecture tests appear in `Automaton.All.sln`; `azure-pipelines-studio-server.yml` uses `Automaton.Server.sln` | Invoke the architecture test project explicitly and verify the actual required PR checks. Other external CI configuration has not been audited. |
| No explicit central complexity policy found | No tracked root `.editorconfig`, `Directory.Build.props`, or Sonar integration found in the inspection | Add a deliberate rule set; do not assume the SDK has no analyzers merely because central configuration is absent. |
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

Use existing .NET analyzers plus a pinned `SonarAnalyzer.CSharp` package for local feedback. Centralize package/configuration policy for applicable projects without replacing project-specific settings. Choose the exact compatible version during implementation and record it with the baseline.

Configure selected diagnostic severities through `.editorconfig`; configure analyzer-specific numeric parameters through the supported mechanism for the pinned analyzer version. Prove configuration with a deliberately violating sample. Do not assume an arbitrary EditorConfig key changes a Sonar threshold.

SonarQube Server or Cloud is optional for history, PR reporting, and centralized quality profiles. The initial gate must work without a hosted service or credentials. Before adopting a service, verify repository integration, current edition capabilities, licensing, and source-data requirements. Avoid duplicate local and scanner analyzer configuration that yields inconsistent results.

### Initial thresholds

These are proposed starting values. Stage 0 calibrates them against representative production code; Stage 1 records the adopted values before enforcement.

| Signal | Initial value | Treatment |
| --- | --- | --- |
| Cognitive complexity | Greater than 15 per method | Block new violations after baseline; prevent existing violating methods from worsening |
| Cyclomatic complexity | Greater than 10 per method | Advisory initially; consider promotion after the pilot |
| Control-flow nesting | More than 3 levels | Review signal; choose a supported analyzer rather than count braces |
| Method size | More than about 60 logical lines | Review signal; document the measurement convention |
| Constructor dependencies | More than 7 injected dependencies | Responsibility review, not a demand for a dependency-bag object |
| File size | More than about 500 lines | Triage signal, not a merge gate |
| Forbidden project/type dependency | Any new violation | Block |
| Duplication | Baseline and inspect changed regions | Advisory until measurement and exclusions are reliable |
| Selected correctness diagnostics | Explicit rule list | Block new violations; no blanket conversion of all warnings to errors |

Use one authoritative diagnostic for each blocking concept. For example, avoid simultaneously failing multiple analyzers for the same complexity issue. Exclude generated code using explicit, reviewed paths or generated-code metadata. Apply appropriate separate settings to tests and fixtures rather than excluding all tests from correctness analysis.

### Existing-debt baseline and comparison

Do not turn the repository red for all historical findings or hide all legacy folders behind exclusions.

1. Record a reviewed baseline at a specific commit with tool versions, rule configuration, exclusions, and a digest of those settings.
2. Identify findings by rule, project-relative path, and stable symbol identity including overload signature. Line numbers are display information, not identity.
3. Fail on new blocking findings. For existing complexity findings, fail when their measured score increases. Report resolved findings separately and remove their exemptions when accepted.
4. Compare whole affected symbols, not only changed lines. A method-level diagnostic may appear on a declaration outside the diff.
5. Treat copied code as new code. Treat renamed/moved findings as new unless a reviewer explicitly approves a baseline identity migration with unchanged or lower debt.
6. Fail clearly on missing baseline, missing merge-base history, incompatible configuration, or failed analysis. Never interpret an empty or incomplete report as a pass.
7. Baseline additions, rule weakening, exclusions, and suppressions require explicit rationale and review. Do not regenerate the baseline automatically during normal verification.

Prefer standard analyzer output and an existing report comparator. If none supports the required comparison, add only a small deterministic adapter under ordinary tooling such as `scripts/quality/`. This is build tooling, not an agent evaluation system; do not add orchestration loops or metrics harnesses under `.agents/` or `AgentRuns/`.

Verify whether the selected analyzer emits numeric scores. If not, use a compatible Roslyn-based measurement tool for the non-worsening comparison. Do not approximate complexity with regular expressions. Until numeric comparison is available, explicitly label non-worsening as a review requirement; do not claim it is automatically enforced.

Baseline membership is not a general suppression. Exceptions need a narrow symbol/rule scope, reason, responsible maintainer, and review date or removal condition. Prefer short-lived exceptions for analyzer limitations over accepting new design debt.

## Delivery stages

### Stage 0 — Inventory and reproducible baseline

Deliverables:

- A short baseline report under `docs/architecture/`, with the measured commit, commands, tool versions, scope, and known gaps.
- A ranked backlog of approximately ten hotspots: symbol, owning feature, complexity, dependencies, recent churn, behavioral coverage, risk, and proposed responsibility split.
- A proposed pilot and a documented reason for selecting it.
- An inventory of solution membership, existing tests, CI entry points, and relevant exclusions.

Work:

1. Capture a clean or explicitly described working-tree state; preserve unrelated changes.
2. Run analysis initially in report-only mode across the affected production projects. Keep raw generated reports out of source control; retain compact reviewed baseline metadata if needed for the gate.
3. Rank complexity together with frequency of change and behavioral risk. Do not rank solely by file length.
4. Inspect the top candidates manually for mixed responsibilities, duplicated decisions, and public contracts.
5. Confirm existing tests and identify missing characterization cases before selecting the first refactor.

Candidate areas include `AppsService`, `RunnerService`, `RunnerHostService`, and hosted-runner lifecycle reconciliation. They are candidates, not a pre-approved rewrite list. `HostedRunnerLifecycleReconcilerTests` and deployment-target tests provide potential starting coverage, but their existence does not establish completeness.

Exit criteria: another developer can reproduce the report; actual method scores replace guesses; thresholds, exclusions, and the pilot have a documented rationale. No production behavior changes in this stage.

### Stage 1 — Local quality command and gradual gate

Depends on Stage 0.

Deliverables: central analyzer settings, scoped analyzer references, a reviewed baseline, and a documented non-mutating quality command under `scripts/quality/`.

Work:

1. Add central configuration, taking care that `Directory.Build.props` settings may affect every descendant project and fixture. Preserve nullable settings, target frameworks, and existing warnings policy.
2. Pin analysis tooling and define a small initial diagnostic allowlist. Ensure both local and CI invocations use it.
3. Implement report-only and enforcement modes. Support an explicit base ref and project/scope selection; define how changed shared projects include dependent projects.
4. Build with analyzers, collect machine-readable findings, compare to the reviewed baseline, and return a nonzero exit code for blocking violations or failed prerequisites.
5. Always run the architecture test project for backend/boundary changes. Run behavioral tests appropriate to the selected scope; support an explicit full verification mode for CI.
6. Add separate frontend `lint:check` scripts without `--fix`. Retain an explicit fixing command for developers. Use existing type checks and tests.
7. Document the exact installed commands and failure output. Avoid host-specific paths, credentials, or implicit downloads of unpinned tools.

The entry point must run on the Windows development environment and Linux CI, either through PowerShell Core where provisioned or a small portable existing runtime. Verify prerequisites rather than assuming a shell is installed.

Exit criteria: clean baseline passes; deliberate complexity and dependency violations fail; unchanged legacy debt passes; increased legacy scores fail if advertised as automated; analysis failures fail; source remains unchanged after verification. Cover rename, overload, deleted symbol, and configuration-change cases in meaningful comparator tests.

### Stage 2 — Required CI and architecture coverage

Depends on Stage 1.

Deliverables: CI quality job(s), published reports, explicit architecture-test execution, and documented required-check configuration.

Work:

1. Wire the local command into the relevant Azure pipeline entry points. Determine coverage across API, Runner, Worker, CLI, Studio, and shared projects; do not assume the server solution covers everything.
2. Invoke `src/UnitTests/Automaton.Architecture.Tests/Automaton.Architecture.Tests.csproj` explicitly, avoiding dependence on incidental solution membership.
3. Align restore/build/test configuration so `--no-build` never consumes a mismatched output. Keep unrelated pipeline modernization out of scope.
4. Include shared configuration, scripts, and architecture tests in path triggers. Establish actual PR behavior for the repository provider and target branch; YAML alone is not proof of branch-policy enforcement.
5. Fetch enough history to resolve the comparison base. Publish reports even when the quality gate fails, without exposing source secrets or runtime data.
6. Make the check required through the repository's actual branch protection/build-validation mechanism when authorized. Record any external configuration still outstanding.
7. Test a deliberate violating PR or equivalent disposable CI change, then remove it. Verify that the required check blocks integration and a clean change passes.

Extend type-level architecture tests only for agreed seams. Use reflection, compiler symbols, or an established architecture library if it provides a clear benefit. Keep XML project-reference tests where they already express the intended rule well. Avoid source substring checks and blanket rules inferred only from class suffixes.

Exit criteria: all relevant change paths trigger the checks; architecture tests demonstrably execute; required-check enforcement is verified or clearly recorded as pending. Do not mark enforcement complete merely because a pipeline YAML file exists.

### Stage 3 — Codex engineering contract and refactoring workflow

Depends on Stage 1; can proceed while Stage 2 integration is completed.

Deliverables: a concise `AGENTS.md` update and one focused repository skill, proposed as `.agents/skills/maintainability/SKILL.md`, explicitly added to the repository skill contract.

This stage names the new skill work intentionally. Follow the skill-creator guidance when implementing it. Do not expand unrelated developer-local skills or add agent roles, loops, or orchestration machinery.

Put durable rules and the quality command in `AGENTS.md`. Put the detailed refactoring procedure and examples in the skill. Avoid duplicating the entire plan in both places. Add the pilot's verified before/after example after Stage 4.

Proposed contract:

> Before extending a service, identify the responsibility being added and its owner. Keep business decisions cohesive and orchestration readable. Follow the owning feature's placement conventions. Preserve observable behavior during cleanup. Run the documented quality command and affected behavioral tests. Do not weaken rules, expand the baseline, or introduce suppressions merely to finish a task. Explain any justified exception explicitly.

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
6. Run the smallest relevant verification after each change, then the quality gate and the affected integration/acceptance checks.
7. Record before/after complexity and dependency evidence together with a readable design explanation. Check total responsibilities and call flow, not only the entry method's score.
8. Document the resulting convention and a compact example in the maintainability skill.

Exit criteria: behavior and public contracts remain stable; the targeted complexity/responsibility problem is reduced; no replacement god-service or helper sprawl appears; the gate passes without new exemptions; review confirms the design is easier to follow.

### Stage 5 — Incremental cleanup batches

Depends on successful Stage 4. Re-rank the backlog after the pilot.

Suggested sequence:

1. Supporting DTO/options placement in the selected features, preserving namespace and serialization compatibility.
2. Repeated pure decisions and validation with clear policy ownership.
3. Query/use-case separation inside broad application services.
4. Lifecycle and deployment orchestration, only with adequate retry/concurrency coverage.
5. Runner process/container integration boundaries, retaining generic-runtime guarantees.
6. Studio composables, service modules, and component responsibility where analysis shows equivalent problems; apply Vue and Studio design skills for those changes.

Each PR should address one coherent responsibility boundary, include its verification evidence, and remove baseline entries it resolves. Keep feature additions and broad formatting changes separate. Do not move unrelated models just because a folder is being edited.

Exit criteria per batch: defined behavior preserved, selected debt reduced, no new blocking findings, appropriate regression tests pass, and remaining work is recorded in the backlog.

### Stage 6 — Routine maintenance

Depends on active enforcement and at least one completed cleanup batch.

- Reserve a small explicit cleanup allocation per development cycle; initially one bounded hotspot or roughly 10% of engineering capacity, adjusted after observing actual cost.
- When changing a violating method, improve or preserve its complexity and clarify nearby ownership within the task's scope. Do not trigger a whole-service rewrite for a small bug fix.
- Review hotspot movement, new exceptions, analyzer noise, and unresolved baseline entries monthly. This cadence is a team practice, not an automation created by this plan.
- Upgrade analyzer versions in dedicated changes with baseline/configuration review; avoid surprise diagnostic changes during feature work.
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

These examples are not a complete verification suite. The unified quality command, baseline comparison, and new frontend check scripts are Stage 1 deliverables and do not exist merely because this plan describes them.

## Change boundaries, handoff, and recovery

- Use the existing checkout and preserve unrelated working-tree changes. Follow the branch instruction for each implementation task.
- Every stage handoff lists implemented files, exact checks and outcomes, unavailable checks, baseline/exemption changes, and remaining acceptance criteria.
- Keep configuration/gate changes and application refactors independently reviewable. Revert a faulty refactor without removing the safeguards.
- If a diagnostic is defective, isolate the smallest justified exception with a removal condition. Do not disable the entire quality job or rebaseline all findings.
- Do not change production state, deploy, or publish Runner images as part of cleanup unless a separate request authorizes it and the production skill is followed.
- Do not claim behavior preservation from compilation alone, or claim CI enforcement from a successful local run alone.

## Completion checklist

- [ ] Stage 0: measured baseline and prioritized backlog recorded.
- [ ] Stage 1: reproducible local analysis and baseline comparison verified, with non-mutating frontend checks.
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
