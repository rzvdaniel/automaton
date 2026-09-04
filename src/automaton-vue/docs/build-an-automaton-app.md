---
title: Build an Automaton app
description: Build, configure, validate, deploy, and publish an Automaton application.
---

# Build an Automaton app

An Automaton app is a project with `automaton.json` and application source. The
source layout and technology are yours to choose.

## Workflow

1. Install and authenticate the CLI.
2. Run `automaton new "<app-name>"` or `automaton link <app-id>`.
3. Inspect `automaton.json` and the generated `AGENTS.md`.
4. Choose a suitable technology and implement the requested app.
5. Configure `application.build`, `application.runtime`, `application.health`,
   and `application.storage` as needed.
6. Run `automaton validate`.
7. Run `automaton deploy` and wait for `Succeeded`.
8. Verify the deployed URL.

The generic contract supports optional build work, a long-running runtime,
HTTP readiness, and durable data. The Runner supplies `AUTOMATON_PORT` and
`AUTOMATON_APP_DATA_DIR` to the process. Publishing is separate and requires
explicit approval.

Read the [automaton.json reference](/automaton-json) for the field contract,
the [CLI reference](/automaton-cli) for commands, and [Deploy](/deploy) for
deployment details.
