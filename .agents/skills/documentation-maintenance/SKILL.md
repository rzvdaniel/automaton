---
name: documentation-maintenance
description: Use when changing repository roadmaps, architecture guides, implementation inventories, or other documentation that tracks system design and delivery status.
license: Apache-2.0
---

# Documentation maintenance

Keep repository documentation accurate, concise, and aligned with implemented behavior and current delivery status.

## Architecture guides

Architecture guides under `docs/architecture/` describe the implemented system. Keep the relevant guide up to date in the same change whenever architecture, state ownership, message flow, runtime boundaries, or operational guarantees change.

Before editing, locate the closest relevant guide and compare its claims with the code and changed behavior. Update only the sections affected by the change. Clearly distinguish current behavior from proposals, assumptions, limitations, and unverified behavior.

## Roadmaps

- Roadmap files hold design and decisions; GitHub issues hold the work. A plan in progress has a parent issue, and each step or follow-up is a sub-issue with full context, acceptance criteria and native *blocked by* links (see the [roadmap index](../../../docs/roadmap/README.md)). Pick work from issues without open blockers. Put new work you find into a new sub-issue, not into the roadmap text, and close the issue from the PR that completes it.
- Update the relevant roadmap in the same change when scope, status, ordering, acceptance evidence, or remaining work changes.
- Record completion with the evidence that supports it: implementation/PR or revision, release state where relevant, validation performed, and any remaining limitation. Do not claim live acceptance from source review or automated tests alone.
- Keep pending work actionable and ordered. State prerequisites, acceptance gates, dependencies, and explicitly deferred work when they affect the next step.
- After a task or slice is complete, remove obsolete next-action text, duplicate task detail, superseded status, and implementation instructions that no longer guide future work. Retain concise decisions, results, provenance, and unresolved limitations needed for audit or later milestones.
- Preserve useful history when it explains a decision or prevents unsafe repetition, but move completed detail into a short evidence record or linked source rather than letting the active plan grow indefinitely.
- Keep separate milestones and acceptance states distinct. Do not let a completed subtask imply completion of a broader milestone.

## General editing

- Prefer updating the existing authoritative document over creating a competing plan.
- Use stable repository-relative links and verify that referenced paths and headings exist.
- Match terminology and status labels already used by the project; remove contradictions introduced by the edit.
- Do not document secrets or unnecessary production identifiers.
- For documentation-only changes, run `git diff --check` and review the resulting diff for accuracy, redundancy, and stale instructions. Run builds only when the repository or task specifically requires them.
