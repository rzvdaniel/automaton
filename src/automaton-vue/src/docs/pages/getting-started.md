# Automaton Documentation

Use this section as the starting point for building and deploying apps in Automaton.

## Recommended path

For most developers, the least-friction workflow is:

1. Install the CLI once.
2. Run `automaton setup`.
3. Let your AI tool build and deploy the app in the current folder.

Manual fallback:

1. Run `automaton new` or `automaton link`.
2. Run `automaton deploy`.
3. Run `automaton validate` only when you need a separate diagnostic check.

Use the CLI + AI workflow, the stack guide, and the starter prompt as the default path for external coding tools.

## What every Automaton app has

- A frontend source project, usually in local `frontend/`.
- A Python backend source project, usually in local `backend/`.
- An `automaton.json` file at the project root.
- A deployment step using the Automaton CLI.

## Current file contract

Keep the model simple:

- Local authoring usually uses `frontend/` and `backend/`.
- Server-side editable files live in `source/frontend/` and `source/backend/`.
- Deployed runtime files live in `live/frontend/` and `live/backend/`.
- Public serving reads only `live/frontend/`.
- Runner execution reads only `live/backend/`.

## Use this documentation menu for

- A quick stack guide for plain HTML/CSS/JS, React, and Vue.
- The in-browser App Editor workflow.
- External AI workflows with CLI and starter prompts.

## Core contract

- Backend controller source files usually live in local `backend/`.
- Each controller exports `handle(action_name, inputs)`.
- Each action returns `{ success, data, error }`.
- Frontend code calls `window.automatonApi.callController(controller, action, inputs)`.
- `callController()` returns `data` directly and throws on error, so use `try/catch`.
