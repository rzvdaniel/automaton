---
title: Build an Automaton App
description: Canonical entry point for what an Automaton app is, which files it needs, the expected folder structure, and where to find the example repo.
---

# Build an Automaton App

If you searched for `Automaton Monster app` or `Automaton app structure`, start here.

This page is the canonical answer to: **what is an Automaton app?**

## App contract

An Automaton app is a project with:

- `automaton.json`
- `frontend/`
- `backend/` (optional)

Typical local structure:

```text
my-app/
  automaton.json
  .automatonignore
  AGENTS.md
  frontend/
    package.json
    src/
    dist/
  backend/
    *.py
```

## Getting started

1. [Install the Automaton CLI](/automaton-cli)
2. Run `automaton login`
3. Run `automaton new "<app-name>"`
4. Build your frontend and optional backend
5. Deploy with `automaton deploy`

Do not invent a generic app structure first. The CLI scaffold and `automaton.json` are the contract.

## What is `automaton.json`?

`automaton.json` is the root config file that tells Automaton:

- which app this folder is linked to
- how to build the frontend
- which frontend directory becomes public runtime
- whether a backend directory should be deployed

Reference:

- [automaton.json Reference](/automaton-json)

## Frontend and backend

- Use [Build a Frontend App](/build-frontend-app) for frontend-only apps.
- Use [Build a Backend App](/build-backend-app) when you need persistence, secrets, or server-side execution.

## Example repo

Canonical full-stack todo example:

- Docs: [Full-Stack Todo Example](/example-todo-fullstack)
- GitHub: [automaton-example-todo-fullstack](https://github.com/automaton-monster/automaton-example-todo-fullstack)

## Related docs

- [CLI & AI Tools](/cli-ai-tools)
- [Deploy](/deploy)
- [Publish](/publish)
