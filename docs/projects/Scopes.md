# Project Scopes and Visibility

This document clarifies how resource visibility works in Automaton across Personal and Project scopes.

## Scopes

- Personal: shows resources you own (WebAppUsers/FlowUsers/etc.), even if they’re also assigned to a project. It’s “mine,” not “unassigned”.
- Project: shows all resources in that project that you have access to via membership (includes items owned by others).

## Visibility

- Owned + in project: visible in Personal and in that Project.
- Owned + not in any project: visible only in Personal.
- Not owned + in project: visible only when you switch to that Project (if you’re a member).
- Not owned + not in any project: not visible to you.

## Invited Users

Invited project members see project resources even if they didn’t create them. Role controls actions (view/edit/execute); access comes from `ProjectMembers`.

## UI Guidance (Minimum)

- The header uses a Project switcher. Selecting `Personal` shows your owned resources. Selecting a specific project shows shared resources in that project.
- In Personal lists, items linked to a project display a small `Project: {name}` badge with a tooltip indicating that deletion is disabled and management happens under the project.
- Deleting a resource is only allowed when it is not linked to any project.

## Backend Notes

- Authorization and filtering are enforced server-side via `ResourceAuthorizationService`.
- App models include `Scope` and `ProjectId` to reflect project linkage; similar patterns will be applied to other resource DTOs.
