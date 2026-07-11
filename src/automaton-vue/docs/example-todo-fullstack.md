---
title: Full-Stack Todo Example
description: Canonical Automaton Monster example project with Vite frontend, FastAPI backend, persistence, deploy, and publish flow.
---

# Full-Stack Todo Example

This is the canonical checked-in example project for Automaton Monster.

It shows:

- `frontend/` Vite TypeScript UI
- `backend/` FastAPI HTTP runtime
- persistent todo storage in a JSON file
- same-origin `/api` usage
- `automaton deploy`
- `automaton publish`

## Example files

Canonical GitHub repository:

```text
https://github.com/automaton-monster/automaton-example-todo-fullstack
```

Key files:

- [Repository root](https://github.com/automaton-monster/automaton-example-todo-fullstack)
- [automaton.json](https://github.com/automaton-monster/automaton-example-todo-fullstack/blob/main/automaton.json)
- [frontend/src/main.ts](https://github.com/automaton-monster/automaton-example-todo-fullstack/blob/main/frontend/src/main.ts)
- [frontend/src/api.ts](https://github.com/automaton-monster/automaton-example-todo-fullstack/blob/main/frontend/src/api.ts)
- [backend/todos.py](https://github.com/automaton-monster/automaton-example-todo-fullstack/blob/main/backend/todos.py)
- [README.md](https://github.com/automaton-monster/automaton-example-todo-fullstack/blob/main/README.md)

## CLI flow

```bash
automaton login
automaton new "Todo Full-Stack"
```

If `automaton new` created a local scaffold but remote creation failed:

```bash
automaton create
automaton deploy
```

## Deploy

```bash
cd frontend
npm install
npm run build

cd ..
automaton deploy
```

## Publish

Only after explicit approval:

```bash
automaton publish --yes
```
