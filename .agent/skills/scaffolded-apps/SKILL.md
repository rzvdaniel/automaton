---
name: scaffolded-apps
description: Use when changing generated Automaton app templates, app scaffolds, llms.txt guidance, generated AGENTS.md files, or AI-facing app creation instructions.
---

# Scaffolded and Generated Apps

Use this skill when touching:

- `automaton new` templates
- generated frontend/backend project structure
- generated `AGENTS.md` or AI instructions inside apps
- `llms.txt` or app-building guidance
- starter apps and examples
- template verification commands

## Default app shape

Generated apps should be easy for AI coding agents to understand.

Default frontend shape:

```txt
frontend/
  package.json
  src/
  public/
  dist/          # build output, not source
backend/         # optional, only when backend requested
automaton.json
AGENTS.md        # optional generated app instructions
```

Rules:

- Do not create a nested app folder inside the project unless explicitly requested.
- Do not put editable source under `dist/`.
- Do not commit or archive `node_modules/`, `dist/`, `.vite/`, `.venv/`, `__pycache__/`, `.git/`, `.env`, logs, or local caches.
- Keep backend optional.
- If backend exists, make runner requirements clear.
- Keep generated instructions short and operational.
- Prefer Vite + TypeScript or JavaScript for frontend scaffolds unless the user requested another framework.

## Generated AGENTS.md guidance

A generated app-level `AGENTS.md` should explain:

- this is an Automaton app
- edit `frontend/` and optional `backend/`
- do not edit build output manually
- run the frontend build before deploy
- use `automaton deploy`
- backend execution requires a runner

Avoid long product explanations in generated app instructions.

## AI-facing app guidance

When updating `llms.txt` or app-building instructions:

- be explicit about folder names
- include copy-pasteable commands
- explain frontend-only versus frontend+backend paths
- mention runner requirements for backend apps
- include deployment command and expected output
- avoid stale workflow automation language when the task is app creation/deployment
- for backend/runtime file storage, instruct apps to use `AUTOMATON_APP_DATA_DIR`
- recommend `media/`, `db/`, `generated/`, `cache/`, `logs/`, and `tmp/` under `AUTOMATON_APP_DATA_DIR`
- explain that only `media/` currently has a platform URL convention via `/media/...`

## Runtime data guidance

Generated AI-facing instructions should treat this as the hard rule:

- all durable runtime app data must go under `AUTOMATON_APP_DATA_DIR`

Recommended convention:

```txt
$AUTOMATON_APP_DATA_DIR/
  media/
  db/
  generated/
  cache/
  logs/
  tmp/
```

Use these folders when they fit. Do not create empty folders by default.

Additional guidance:

- use `media/` for uploaded images, documents, avatars, and files the app may reference with `/media/...`
- use `db/` for SQLite and other durable structured storage
- use `generated/` for reports, exports, PDFs, CSVs, and generated documents
- use `cache/` for disposable cached data
- use `logs/` only when the app truly needs app-owned log files
- use `tmp/` for temporary files that are safe to delete
- do not write runtime data into `frontend/public`, `frontend/dist`, `wwwroot`, `live/frontend`, or `live/backend`

## Verification

For scaffold changes, verify:

```bash
cd <generated-app>/frontend
npm install
npm run build
```

If backend is generated, verify the backend entry point and dependency file are present.

For CLI scaffold changes, also run:

```bash
dotnet test src/UnitTests/Automaton.Cli.Tests/Automaton.Cli.Tests.csproj
```

Read:

```txt
.agent/harness/verification.md
```
