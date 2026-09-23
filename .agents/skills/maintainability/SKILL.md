---
name: maintainability
description: Use when changing Automaton-owned code where method complexity, class coupling, service responsibilities, or supporting-type ownership matter.
---

# Maintainability

Before extending a large service, identify the use case and the current owner of its decisions, persistence, and integrations. Keep the change with a cohesive owner; do not move types merely to make folders look tidy.

Run `python scripts/quality/complexity_gate.py` for .NET changes. It builds every tracked C# project and rejects new or worsened `CA1502`, `CA1506`, and `S3776` findings against the reviewed baseline. Use `--report-only` to list the current inventory and `--require-zero` to verify the eventual debt-free target. Do not edit the baseline or weaken thresholds just to make a change pass.

For substantial refactors, add or identify behavioral tests before extracting code. Preserve public contracts, query behavior, cancellation, transaction boundaries, ordering, and resource lifetimes. Prefer a private method or a cohesive collaborator over a general-purpose utility/model bucket. Do not split methods into trivial helpers or create interfaces without a useful boundary. After the change, compare the design and total responsibilities, not only the reported score.
