---
name: maintainability
description: Use when changing Automaton-owned code where method complexity, class coupling, service responsibilities, or supporting-type ownership matter.
---

# Maintainability

Before extending a large service, identify the use case and the current owner of its decisions, persistence, and integrations. Keep each class cohesive around its responsibilities; split classes when responsibilities belong to distinct concerns, not just to reduce class size or type coupling. Keep cyclomatic complexity below 15, and simplify or extract cohesive responsibilities when a method reaches that limit without splitting one algorithm into meaningless helpers.

`CA1502` is a build error. For C# changes, build the affected project with `dotnet build <project.csproj> -c Debug --no-incremental -v minimal` and resolve its analyzer errors. Before handoff, run `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal` to compile every tracked C# project, including tests and fixtures. Existing debt is intentionally reported as build errors until cleanup resolves it; do not weaken thresholds or add suppressions to make a build pass.

For substantial refactors, add or identify behavioral tests before extracting code. Preserve public contracts, query behavior, cancellation, transaction boundaries, ordering, and resource lifetimes. Prefer a private method or a cohesive collaborator over a general-purpose utility/model bucket. Do not split methods into trivial helpers or create interfaces without a useful boundary. After the change, compare the design and total responsibilities, not only the reported score.
