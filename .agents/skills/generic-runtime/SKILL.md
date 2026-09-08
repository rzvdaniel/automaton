---
name: generic-runtime
description: Use when changing Automaton app source and runtime behavior, deployment contracts, public serving, archives, forks, remixes, or runtime acceptance.
license: Apache-2.0
---

# Generic runtime

Use this skill for app source and runtime changes across the CLI, Studio, API, Runner, publishing, archive, fork, and remix flows.

## Durable rules

- Editable app content belongs under `source/`.
- Deployed output belongs under `live/`. Never edit `live/` as source.
- Do not introduce language-, framework-, frontend-, or backend-specific assumptions into generic runtime boundaries.
- Legacy role-specific paths and controller behavior are compatibility surfaces only. Do not extend them into new generic-runtime architecture.
- Public serving must stay within the intended application runtime boundary and must not expose secrets, logs, runtime state, durable private data, or other unintended files.
- Forks and remixes copy portable editable source, preserve attribution, clear runner assignment, and never copy deployed runtime output.
- Preserve `automaton.json` link metadata.

Within the application source payload, Automaton does not prescribe a language, framework, package manager, internal source layout, build system, or application architecture.

## Application contract

The application contract is implemented by the CLI and Runner. Read the current code before changing it:

- `src/Automaton.Cli/Services/DeployService.cs`
- `src/Automaton.Runner/Services/RunnerRuntimeProcessManager.cs`
- `src/UnitTests/GenericRuntimeAcceptanceFixtures/`

The build command is optional. A web application must declare a runtime command and the process must listen on the effective `AUTOMATON_PORT`. Actions-only or finite-work applications without an HTTP endpoint may omit the runtime command.

When a Generic Runtime change requires a new Automaton.Runner image, follow the
production-deployment skill for Runner build and release provenance.

Runtime-created durable data belongs under `AUTOMATON_APP_DATA_DIR`. Build output, runtime artifacts, caches, and dependencies are disposable. Prefer structured commands and keep managed paths inside the application workspace.

## Verification

Validate the application with `automaton validate`, deploy with `automaton deploy`, wait for the asynchronous operation to reach `Succeeded`, and verify the deployed URL. Do not report success from a submitted operation alone.

For source, archive, fork, or public-serving changes, verify source/runtime separation, exclusion of generated and sensitive files, and protection of backend paths. Use the relevant tests under `src/UnitTests/`.
