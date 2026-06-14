---
name: studio-frontend
description: Use when changing the Automaton Studio Vue frontend, including views, components, routing, stores, services, admin screens, app editor, app details, marketplace, or publishing UI.
---

# Studio Frontend

Use this skill when touching:

- `src/automaton-studio-vue/**`
- Vue views/components
- Pinia stores
- routing
- API service modules
- app editor, app details, screenshots, publishing, marketplace, admin, runners, API keys, or AI keys UI

## Architecture rules

- Use feature-based structure under `src/features/`.
- Keep shared reusable code under `src/shared/`.
- Keep stores lean.
- Keep API calls inside feature services or shared API services.
- Use generated API types when available.
- Prefer const objects over TypeScript enums when values must match backend serialized strings.
- Avoid scattering magic strings for modes, statuses, roles, providers, and event types.

## Component rules

- Keep page components readable.
- Split large role/state conditionals into focused components.
- Use `.agent/skills/vue-component-splitting/SKILL.md` when a component has heavy conditional rendering.
- Avoid duplicate global providers such as duplicate toast hosts.
- Avoid hiding business rules in templates.
- Use composables for shared stateful logic.
- Prefer clear loading, empty, error, and unauthorized states.
- Check mobile behavior when changing layout.

## API and state rules

- Keep request/response typing explicit.
- Match backend DTO shape and naming.
- Avoid direct Axios calls from random components when a service already exists.
- Handle API errors in user-visible ways where appropriate.
- Do not leak tokens, provider keys, or raw API keys in UI state, logs, or errors.

## Verification

Run:

```bash
cd src/automaton-studio-vue
npm run type-check
npm run build
```

If dependencies changed, run `npm install` first.

For UI changes, verify or reason through:

- desktop layout
- mobile layout
- loading state
- empty state
- error state
- unauthorized/forbidden state
- long names/text
- missing images/media
- dialog close behavior and keyboard basics

Read:

```txt
.agent/harness/verification.md
```
