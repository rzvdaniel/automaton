---
name: automaton-cli
description: Use when changing Automaton CLI commands, app scaffolding, pull/deploy/publish behavior, source archives, or automaton.json handling.
---

# Automaton CLI

Use this skill when touching:

- `src/Automaton.Cli/**`
- `automaton new`, `setup`, `pull`, `deploy`, or `publish`
- `automaton.json`
- source archive creation
- deploy packaging
- generated local app scaffolds

## Rules

- Keep CLI behavior scriptable and friendly to AI coding agents.
- JSON and other non-interactive modes must never prompt; resolve available credentials and return a structured failure when required input is missing.
- Preserve the source/runtime split.
- Do not include dependency folders, build output, caches, local environment files, secrets, logs, or VCS folders in source archives.
- Treat `automaton.json` as the local link contract. Preserve it unless the command intentionally relinks the project.
- Prefer clear actionable errors over generic failures.
- On successful deploy or publish, surface the resulting URL in user-facing output.
- Do not make interactive prompts mandatory for flows that agents need to run non-interactively; provide flags or safe defaults where practical.

## Source archive exclusions

- Keep hardcoded exclusions limited to Automaton-local state and VCS metadata that
  must never be uploaded.
- Put framework, dependency, build, environment, log, and database defaults in the
  scaffolded `.automatonignore` so projects can adjust them.
- Treat `.automatonignore` as independent from `.gitignore`; never inherit Git
  exclusions silently.
- Support Git-style root-relative patterns, directory patterns, globs, and negation.
- Preserve `automaton.json` in editable source snapshots while excluding it from
  runtime-only uploads.
- Do not automatically exclude `backend.deployDir` from editable source: in the
  standard project model it is also `source/backend/`. Use explicit `source.exclude`
  rules for genuinely generated runtime directories.

When changing archive behavior, test nested exclusions, not only root-level folders.

## Pull behavior

`automaton pull` should prefer editable source content.

Verify:

- existing linked projects still pull correctly
- pull into a fresh folder creates or preserves `automaton.json` correctly
- local files are not overwritten silently
- `source/frontend/` and `source/backend/` are preferred over runtime output
- backend runtime files, secrets, logs, runner state, and user data are not pulled as editable source

## Deploy behavior

Verify:

- frontend deploy uses the configured build output
- backend deploy uses the intended backend source path
- backend deploy requires a runner when appropriate
- deploy packaging excludes dependencies, generated output, caches, secrets, and local-only files
- successful deploy output includes where to open the app

## Verification

Run:

```bash
dotnet test src/UnitTests/Automaton.Cli.Tests/Automaton.Cli.Tests.csproj
```

For archive/deploy changes, add or update targeted tests for inclusion/exclusion rules and source/runtime separation.

For the full matrix, read:

```txt
.agent/harness/verification.md
```
