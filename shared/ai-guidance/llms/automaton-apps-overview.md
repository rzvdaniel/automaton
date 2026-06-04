# Automaton Apps Overview

Automaton apps are frontend-agnostic static web apps with optional Python backends.

## AI shortest path

For a new app:

```bash
automaton login
automaton new "My App"
automaton deploy
```

Rules:

- `automaton login` authenticates the CLI. It does not scaffold app files.
- `automaton new` creates the local app scaffold, `automaton.json`, `.automatonignore`, and local AI guidance.
- `automaton link <app-id>` is only for attaching a folder to an existing Automaton app.
- Ask before `automaton publish --yes`.

## Core rules

- Keep `automaton.json` at the project root and commit it to Git.
- Treat the local project as source. Frontend source normally lives in `frontend/`; backend source lives in `backend/`.
- `automaton deploy` uploads `frontend.deployDir` into `runtime/frontend/` and `backend.deployDir` into `runtime/backend/` when present.
- Only `runtime/frontend/` is public. Source files and backend runtime files are never publicly served.
- If `automaton.json` already exists and the app uses a build step, add missing `frontend.build` and `frontend.deployDir` fields instead of leaving deploy behavior implicit.
- If the app is build-based and also has Python backend controllers, keep backend source in `backend/`; do not copy it into frontend `deployDir`.

## Frontend defaults

- Frontend code for AI-generated local apps should default to Vite vanilla TypeScript or JavaScript under `frontend/`.
- AI-generated local apps should always use a frontend build step and deploy only `frontend/dist/` as public runtime.
- Create `frontend/vite.config.ts` or `frontend/vite.config.js` and set `base: './'` explicitly.
- After `vite build`, verify `frontend/dist/index.html` uses relative app-local asset paths such as `./assets/...`; never use root-relative paths such as `/assets/...`.
- Do not move Vite output to root `dist/` unless the user explicitly requested a no-build static app.

## Backend defaults

- Default to frontend-only.
- Create `backend/` only when the requested feature truly requires server-side execution, secrets, persistence, auth, scheduled work, or browser-unsafe access.
- Do not create a backend for convenience, caching, proxying, optional enrichment, CORS workarounds, or mixed-content fixes.
- Backend controllers must export `handle(action_name, inputs)`.
- Do not create FastAPI, Flask, Express, or other standalone HTTP servers.
- Do not create empty `backend/` folders for frontend-only apps.

## Recommended workflow

1. Run `automaton login`.
2. If this is a new app, run `automaton new "My App"`.
3. If this is an existing app, run `automaton link <app-id>`.
4. Follow the generated local guidance in `AGENTS.md` and `.automaton/agents/`.
5. Build locally.
6. Test locally first.
7. Run `automaton deploy`.
8. Preview and inspect logs in Automaton Studio.
9. Publish only after user approval.

Do not consider local build, type-check, tests, or `python -m py_compile` to be completion. Before declaring the app done, run `automaton deploy`; if it fails, report the exact command, exact error, and the next step.

## Canonical autonomous order

```bash
automaton login
automaton new "My App"
automaton deploy
# ask user before publish
automaton publish --yes
automaton status
```

Use the linked files in this folder for exact backend, frontend, deploy, and starter-prompt rules.
