---
name: admin-docs
description: Use when changing or documenting administrator-visible Studio and API workflows, permissions, infrastructure, runners, plans, settings, or operational troubleshooting.
license: Apache-2.0
---

# Admin documentation

Use this skill when a change affects what an Automaton administrator sees or does.

## Workflow

1. Inspect the implementation in `src/automaton-studio-vue/` and `src/Automaton.Api/`.
2. Locate the closest page under `src/Automaton.Api/Documentation/Admin/` and its navigation.
3. Verify the exact route, navigation label, visible control text, permission, API behavior, status, warning, and consequence.
4. Extend the closest page for a small behavior change. Add a page only for a distinct workflow.
5. Update navigation and contextual links with the page.
6. Validate links and claims against the final implementation.

Write short, task-focused instructions. State prerequisites, permissions, expected results, and destructive consequences. Distinguish actions performed by Studio from commands an administrator runs on a host. Never invent controls, routes, commands, guarantees, or statuses. Never include secrets or real host identifiers.

If the change has no administrator-visible effect, do not change the docs. State that conclusion in the handoff.
