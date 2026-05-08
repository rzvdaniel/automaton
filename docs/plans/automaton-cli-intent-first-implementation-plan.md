# Automaton CLI Intent-First Implementation Plan

## Goal

Implement the intent-first CLI onboarding flow described in `/docs/specs/automaton-cli-intent-first-setup.md` without breaking existing commands or automation scripts.

This plan maps the new UX onto the current command surface in `src/Automaton.Cli`.

## Current CLI Surface

Current top-level commands in `Program.cs`:

- `login`
- `logout`
- `whoami`
- `create`
- `status`
- `current-runner`
- `validate`
- `init`
- `link`
- `deploy`
- `pull`
- `publish`
- `runners`
- `set-runner`
- `config`

Current gap:

The CLI exposes low-level setup and deploy primitives, but does not expose a small intent-level onboarding surface.

## Target UX

Happy path for a first-time user:

```bash
automaton setup
automaton new "My App"
automaton deploy
```

Happy path for linking an existing app:

```bash
automaton setup
automaton link <app-id>
automaton deploy
```

Design rule:

- gather missing information inside these commands
- keep low-level commands available, but not required for normal onboarding

## Reuse Strategy

Do not rewrite the CLI from scratch.

Implement the new flow as orchestration over the existing commands and services where possible.

### Existing assets to reuse

- `ConfigService` for config resolution and credential persistence
- `CliCommandChecks` and related diagnostics for project-link and credentials checks
- `ValidationService` for preflight logic
- `DeployService` for upload/backend deploy behavior
- `CreateCommand`, `InitCommand`, and `LinkCommand` logic for app creation/linkage
- `LoginCommand` / auth persistence logic

## Proposed New Commands

## 1. `automaton setup`

### Intent

Prepare this machine or workspace for Automaton development.

### Responsibilities

- default server to `https://automaton.monster`
- detect whether the user is already authenticated
- if not authenticated, run the login flow inside setup
- persist default local config if needed
- optionally detect whether this directory looks like a workspace root
- optionally install local AI guidance later
- end with a concise ready-state summary

### Suggested implementation

Add:

- `SetupCommand.cs`
- `SetupService.cs`
- `SetupResult.cs`

`SetupService` should orchestrate:

1. resolve or apply default server
2. detect existing credentials
3. if missing, call or reuse login flow
4. optionally record workspace mode in local config later
5. emit a structured result in the same `success / summary / checks / nextSteps` shape already used elsewhere

### Internal behavior

Do not ask for the server in the standard hosted path.

Only surface a server override when explicitly requested, for example via:

```bash
automaton setup --server https://custom-host
```

## 2. `automaton new`

### Intent

Create a new remote app and link the current folder.

### Responsibilities

- create the remote app
- write `automaton.json`
- create `.automatonignore` if missing
- ask for app name if not provided
- optionally scaffold starter files later

### Suggested implementation

Add:

- `NewCommand.cs`
- `NewAppService.cs`
- `NewAppResult.cs`

Reuse logic currently spread across:

- `CreateCommand`
- `InitCommand`
- `LinkCommand`

Preferred direction:

- keep `create` and `init` for compatibility
- make `new` the default user-facing orchestrator
- eventually make `create` and `init` feel advanced/internal in docs

### Internal behavior

If the user runs:

```bash
automaton new
```

prompt for app name interactively.

If the user runs:

```bash
automaton new "My App"
```

skip the prompt and proceed directly.

## 3. Improve `automaton link`

### Intent

Attach the current folder to an existing app with minimal friction.

### Responsibilities

- prompt for app ID if missing
- write `automaton.json`
- create `.automatonignore`
- optionally offer to pull remote files if the folder appears empty or incomplete

### Suggested implementation

Prefer extending the existing `LinkCommand` and shared link/init service logic instead of introducing a second link path.

## 4. Upgrade `automaton deploy`

### Intent

Make deploy the high-level ship command instead of expecting manual preflight orchestration.

### Responsibilities

- run internal validation first or equivalent checks
- detect backend presence automatically
- if no backend controllers exist, skip runner logic entirely
- if backend exists, verify runner assignment
- if runner assignment is missing, offer connected runners for selection or accept `--runner`; if none exist, stop and tell the user to create or deploy one outside the CLI
- deploy only after the preconditions are satisfied or explicitly declined

### Suggested implementation

Keep `DeployService` as the execution core.

Add a thin orchestration layer before calling `DeployService.DeployAsync(...)`:

1. run validation service or equivalent prerequisite checks
2. detect whether backend controllers exist
3. if backend exists and no runner is assigned:
  - show connected runners and allow selection
  - if no connected runners exist, stop early with guidance to create or deploy one outside the CLI
4. proceed to deploy

Important:

- `validate` remains available as a separate command
- `deploy` should use validation internally on the happy path

## Refactoring Plan

## Phase 1: Introduce shared orchestration services

Create services that are not tied directly to one command class:

- `SetupService`
- `NewAppService`
- possibly `RunnerPreparationService`

Goal:

Move interactive, high-level orchestration out of command classes so the new commands remain testable.

## Phase 2: Add `setup`

Wire `SetupCommand.Build(config)` into `Program.cs`.

Deliverables:

- hosted server defaulting
- embedded login flow
- structured JSON/human output
- tests for authenticated and unauthenticated flows

## Phase 3: Add `new`

Wire `NewCommand.Build(config)` into `Program.cs`.

Deliverables:

- name prompt if omitted
- remote app creation
- `automaton.json` creation
- `.automatonignore` creation
- structured JSON/human output
- tests for interactive and non-interactive paths

## Phase 4: Improve `link`

Deliverables:

- prompt for app ID when omitted
- optional pull suggestion or prompt for incomplete folders
- tests for empty folder and existing folder cases

## Phase 5: Upgrade `deploy`

Deliverables:

- internal preflight before deploy
- backend detection
- runner auto-fix prompt when appropriate
- tests for:
  - frontend-only app
  - backend app with assigned runner
  - backend app with missing runner and accepted auto-fix
  - backend app with missing runner and declined auto-fix

## Interaction Model

## Prompting rules

Prompt only when required for the current intent.

### `setup`

Allowed prompts:

- sign in now?
- auth flow choice if there are multiple flows
- optional workspace-prep choices later

### `new`

Allowed prompts:

- app name
- optional scaffold choices later

### `link`

Allowed prompts:

- app ID
- whether to pull remote files if the folder is empty

### `deploy`

Allowed prompts:

- backend detected; no runner assigned; attempt local runner setup?
- destructive confirmation for `--clean`

## JSON Contract

All new commands should use the same machine-readable shape already introduced elsewhere:

```json
{
  "success": true,
  "summary": "...",
  "checks": [],
  "nextSteps": []
}
```

Where relevant, include command-specific metadata such as:

- `server`
- `appId`
- `appName`
- `workspaceMode`
- `runnerPrepared`

## Compatibility Rules

1. Keep all existing commands working.
2. Do not remove `create`, `init`, `login`, `whoami`, `validate`, or runner commands.
3. Do not break existing scriptable non-interactive flows.
4. Add flags like `--yes`, `--non-interactive`, or explicit parameters where automation needs to bypass prompts.

## Testing Plan

## Command-level tests

Add tests similar to the existing CLI JSON shape coverage for:

- `setup --json`
- `new --json`
- `link --json` with prompted/argument forms where practical
- `deploy --json` with integrated preflight branches

## Service-level tests

Add narrow tests for:

- hosted-server default application
- login required vs already authenticated
- app creation result mapping
- deploy orchestration for frontend-only vs backend apps
- runner auto-fix decision branches

## Documentation Plan

After implementation begins, update:

- public CLI docs
- `llms.txt`
- `automaton-cli-deploy.md`
- local workflow docs in `scripts/README.md` if command recommendations change

New recommended onboarding examples should prefer:

```bash
automaton setup
automaton new "My App"
automaton deploy
```

## Open Design Decisions

1. Should `automaton new` eventually absorb the user-facing role of both `create` and `init`, while keeping those older commands for compatibility?
2. Should `automaton deploy` always run the equivalent of `validate`, or only when interactive?
3. Should `setup` install local AI guidance in phase 1, or wait for a later feature?
4. Should workspace-level setup be represented in config, or inferred each time from directory structure?
5. Should `link` automatically pull files for empty folders, or ask first?

## Recommended First Slice

If implementation starts now, the highest-value first slice is:

1. add `automaton setup` with hosted default + embedded login
2. add `automaton new` as a create/link wrapper with app-name prompt
3. leave deeper deploy orchestration for the next slice

That sequence delivers a materially simpler onboarding flow quickly without needing the full runner/deploy orchestration redesign in the first pass.
