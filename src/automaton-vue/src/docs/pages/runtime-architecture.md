# Runtime Architecture

This page is for advanced users.

Most users do **not** need this model to build and deploy an app successfully.

## Local authoring

Local app projects usually use:

```text
frontend/
backend/
automaton.json
```

## Server-side model

Automaton keeps editable files separate from deployed runtime:

```text
source/frontend
source/backend
live/frontend
live/backend
```

## Mental model

- `source/*` = editable project files
- `live/frontend` = public frontend runtime
- `live/backend` = backend runtime used for execution

## Why this exists

- Editable source stays separate from deployed output
- Public frontend files stay separate from backend runtime
- Deploy and publish stay predictable

If you are just trying to ship an app, go back to **Getting Started**, **Build with AI**, or **Deploy**.
