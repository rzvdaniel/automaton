---
title: "Automaton CLI Deploy"
description: "Deploy and runtime troubleshooting guidance for Automaton apps."
---

# Automaton CLI Deploy Workflow

Use this when an Automaton app already exists locally and needs to be deployed with the CLI.

## Required state

Before deploy:

- `automaton login` has completed.
- `automaton.json` exists in the project root.
- New apps should be created with `automaton new "My App"`.
- Existing apps should be linked with `automaton link <app-id>`.
- Follow the generated local guidance in `AGENTS.md` and `.automaton/agents/` when present. Use `https://automaton.monster/llms.txt` only as the fallback source of truth.

## Deploy

```bash
automaton deploy
```

`automaton deploy`:

- runs `frontend.build` when configured
- uploads `frontend.deployDir` to frontend runtime
- uploads `backend.deployDir` to backend runtime when present
- handles runner selection only when backend controllers exist

For backend apps, let deploy choose or prompt for a connected runner:

```bash
automaton deploy
automaton deploy --runner <runner-id-or-name>
```

If backend controllers exist and no connected runner is available, ask for confirmation and prefer:

```bash
automaton runner install
```

## Completion rule

Do not say the app is done until `automaton deploy` succeeds.

Local build, type-check, tests, or `python -m py_compile` are useful checks, but they are not Automaton deployment completion.

If deploy fails, report:

1. the exact command
2. the exact error
3. the next required step

Prefer checking status after deploy:

```bash
automaton status
```

## Publish

Do not publish unless the user explicitly approves.

After approval:

```bash
automaton publish --yes
```

For AI, CI, and scripted workflows, prefer explicit non-interactive commands:

```bash
automaton deploy --non-interactive
automaton publish --yes --non-interactive
```

## Useful deploy options

```bash
automaton deploy --json
automaton deploy --clean
automaton deploy --skip-backend
automaton deploy --runner <runner-id-or-name>
```

## Troubleshooting

`automaton validate` is optional. `automaton deploy` runs the deploy checks needed for the happy path.

If `automaton validate` passes but `automaton deploy` fails during upload with errors such as closed stream, request stream, multipart, socket, timeout, or HTTP transport failure, treat it as likely CLI/server upload-path trouble first.

Do not repeatedly restructure the app unless the error clearly points to `frontend.deployDir`, `backend.deployDir`, archive shape, or duplicate deploy roots.

## Source/runtime reminder

Only files inside `frontend.deployDir` become public runtime.

Do not expect these to appear in the public app runtime:

- `frontend/src/`
- `frontend/package.json`
- `frontend/vite.config.*`
- `backend/`
- local project files

Backend files are uploaded separately to backend runtime when `backend.deployDir` is configured.
