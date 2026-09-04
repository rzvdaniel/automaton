# Runtime Architecture

This page is for advanced users.

Most users do **not** need this model to build and deploy an app successfully.

## Local authoring

Local app projects contain editable source and an `automaton.json` contract. The source layout is chosen by the app and its build tools.

```text
automaton.json
app-source/
```

## Server-side model

Automaton keeps editable files separate from deployed runtime:

```text
source/*
live/*
```

## Mental model

- `source/*` = editable project files
- `live/*` = deployed runtime output
- server-side execution remains separate from public static files

## Why this exists

- Editable source stays separate from deployed output
- Public files stay separate from server runtime
- Deploy and publish stay predictable

If you are just trying to ship an app, go back to **Getting Started**, **Build with AI**, or **Deploy**.
