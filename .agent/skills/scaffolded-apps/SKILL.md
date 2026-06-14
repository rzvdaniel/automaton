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
