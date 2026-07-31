---
name: automaton-admin-docs
description: Use when changing, reviewing, or documenting Automaton Studio administrator workflows, including Admin navigation, pages, actions, permissions, infrastructure, runners, plans, settings, and operational troubleshooting.
license: Apache-2.0
compatibility: Repo-local skill for agents that can read .agent/skills.
metadata:
  author: Automaton
  version: 1.0.0
---

# Automaton Admin Docs

Keep administrator documentation practical, accurate, and synchronized with the implemented Studio and API behavior.

## Workflow

1. Read `AGENTS.md`, `.agent/maps/automaton.md`, and the relevant frontend/backend skills.
2. Locate the existing Admin documentation under `src/Automaton.Api/Documentation/Admin/`, its discovery/navigation rules, and nearby pages.
3. Inspect the implementation before writing. Verify exact routes, navigation labels, visible button/dialog text, permissions, API behavior, generated commands, statuses, warnings, and failure states.
4. Decide the documentation impact:
   - Add a page for a new administrator workflow with several steps or distinct decisions.
   - Extend the closest page for a small action or field.
   - Update every affected page when behavior, terminology, navigation, permissions, prerequisites, or consequences change.
   - Make no documentation edit only when there is no administrator-visible effect; explain that conclusion in the handoff.
5. Update documentation navigation and contextual links in the same change when the repository supports them.
6. Validate the documentation against the final implementation. Check links, image assets, stale terminology, and generated instructions.

## Writing standard

- Lead with where to navigate and what to select.
- Use numbered steps in execution order and exact visible labels in **bold**.
- State prerequisites, required permissions, expected results, and a short verification checklist.
- Explain destructive or identity-changing consequences immediately before confirmation.
- Add troubleshooting only for realistic failure states exposed by the product.
- Prefer short sentences and concrete actions; omit architecture history, theory, and roadmap discussion.
- Never invent controls, routes, commands, automatic behavior, or guarantees.
- Never include secrets, enrollment keys, API keys, credentials, or real host identifiers.

## Page shape

Use the established page structure. Otherwise use only the useful sections: purpose, prerequisites, navigation path, procedure, expected result, troubleshooting, and cautions. For related operations, include a compact decision table.

## Commands and generated instructions

- Tell administrators to copy the current command from Studio when it is environment-specific, signed, checksum-verified, or version-dependent.
- If a command must appear in prose, derive and verify it from the implementation, including syntax, privileges, cleanup, checksum behavior, and failure handling.
- Distinguish actions performed by Studio from actions performed manually on a host.
- Never claim Studio initiated or completed an operation when it only provided instructions or observed the result.

## Infrastructure Runner terminology

| Operation | Administrator intent | Identity and configuration |
| --- | --- | --- |
| Install and enroll | Add an Infrastructure Runner to a new server | Created |
| Update / Repair | Update, reinstall, or recover its container | Preserved |
| Replace / Re-enroll | Replace invalid, lost, or compromised identity or credentials | Recreated |
| Revoke | Retire or block the installation | Disabled |

For **Update / Repair**, document the current Studio-generated checksum-verified command rather than a copied static command. Explain that the host performs replacement locally while Automaton observes disconnect, reconnect, health, readiness, and installed digest. Existing Hosted Runner containers normally continue operating, while new placement and host workflows are unavailable when the Infrastructure Runner is offline or not ready.

## Completion checklist

- Documentation location and navigation title match the established Admin hierarchy.
- Every visible label, route, permission, and status matches Studio and the API.
- Prerequisites and destructive consequences are explicit.
- Generated commands are not duplicated as stale static instructions.
- Related pages, image assets, and contextual links are updated.
- Documentation validation or the relevant frontend/API build passes, or any unavailable check is reported.
- The handoff names documentation files changed, or explains why none were needed.
