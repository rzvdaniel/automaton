---
name: maintainability
description: Use when changing Automaton-owned code where method complexity, class coupling, service responsibilities, or supporting-type ownership matter.
---

# Maintainability

Before extending a large service, identify the use case and the current owner of its decisions, persistence, and integrations. Keep each class cohesive around its responsibilities; split classes when responsibilities belong to distinct concerns, not just to reduce class size or type coupling. Keep cyclomatic complexity below 15, and simplify or extract cohesive responsibilities when a method reaches that limit without splitting one algorithm into meaningless helpers.

`CA1502` is a build error. For C# changes, build the affected project with `dotnet build <project.csproj> -c Debug --no-incremental -v minimal` and resolve its analyzer errors. Before handoff, run `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal` to compile every tracked C# project, including tests and fixtures. Existing debt is intentionally reported as build errors until cleanup resolves it; do not weaken thresholds or add suppressions to make a build pass.

For substantial refactors, add or identify behavioral tests before extracting code. Preserve public contracts, query behavior, cancellation, transaction boundaries, ordering, and resource lifetimes. Prefer a private method or a cohesive collaborator over a general-purpose utility/model bucket. Do not split methods into trivial helpers or create interfaces without a useful boundary. After the change, compare the design and total responsibilities, not only the reported score.

## Keep implementation simple

- Before substantial work, briefly state the required outcome and what completes the task. Reuse the existing task or roadmap; do not create another plan or report by default.
- Choose the smallest complete change through existing code paths. Add an abstraction, interface, service, configuration option, or lifecycle state only when a concrete requirement or actual consumers justify it.
- Do not build speculative provider support, frameworks, compatibility layers, or future milestones. Prioritize a working user flow over disconnected preparation slices.
- Address concrete security and correctness failures with the simplest effective boundary. Preserve isolation, authorization, data integrity, and recovery guarantees; simplicity does not justify weakening them.
- Keep validation proportional to the change: run required builds and focused tests of changed behavior and identified failure cases. Broaden or repeat checks only for a specific unresolved concern. Record unavailable checks and agreed deferred acceptance accurately without creating additional gates.
- Before handoff, review the diff for unnecessary helpers, configuration, documentation, and indirection introduced by the change, and remove what the task does not need.
- If scope grows substantially, explain the concrete reason and simplest viable approach. Do not silently turn a bounded fix into a redesign or add approval stages for routine implementation choices.
- When writing implementation prompts, describe the outcome, constraints, concrete failure, and acceptance evidence. Let the implementing agent choose the smallest safe solution; prescribe architecture only when an established contract or verified need requires it. Distinguish required fixes from optional improvements in reviews.

## Guardrails

- Before extending a service, identify the feature and responsibility that own the new behavior. Keep application services focused on coordinating use cases; place cohesive business decisions and invariants in their owning object or policy.
- Do not add unrelated DTOs, options, or general utilities to a service file or a shared `Services` folder. Keep supporting types with the feature that owns them; share a helper only when real consumers need the same semantics.
- Prefer composition and clear contracts. Add an interface for an actual boundary or variation point, not automatically for every class. Pure functions and data-only transport models are appropriate when they fit.
- Treat .NET analyzer findings as review input, including CA1502 complexity and CA1501 inheritance. CA1502 is a build error and methods should stay below cyclomatic complexity 15. Keep classes cohesive around their responsibilities; split them when responsibilities belong to distinct concerns, not just to reduce class size or type coupling. Do not hide complexity by splitting one algorithm into meaningless helpers.
- Triage analyzer output in this order: build errors; security and correctness warnings affecting changed behavior; complexity and coupling in changed symbols; other warnings. Review high-risk findings even when their rule is not yet a build error. The rule choices are documented in `docs/archive/architecture/analyzer-priority-baseline.md`.
- When changing a large service, check its responsibilities and dependency list, then make the smallest cohesive improvement within the task's scope. Preserve behavior, public contracts, cancellation, query semantics, transactions, and concurrency guarantees.
- For C# changes, build the affected project with `dotnet build <affected-project.csproj> -c Debug --no-incremental -v minimal` before editing and save its analyzer diagnostics for the files and symbols you will touch. Run it again after editing and resolve the relevant build errors. Before handoff, run `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal`; this solution includes every tracked C# project under `src/`, including tests and fixtures. Incremental builds may omit diagnostics from skipped projects, so use `--no-incremental` when checking analyzer output.
- Resolve analyzer errors in changed code. The all-project build currently reports historical complexity debt too; make progress on those findings in scoped, behavior-preserving batches. If unrelated historical findings block a focused build, identify them explicitly rather than suppressing them. If a justified exception remains, explain the finding, reason, and scope in the handoff.
- Run affected tests and review the diff after the analyzer check. Report the before/after findings relevant to the change and any unavailable check. Do not weaken analyzer settings, change thresholds, or add suppressions merely to complete a task.
