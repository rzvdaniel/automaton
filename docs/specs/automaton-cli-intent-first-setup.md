# Automaton CLI Intent-First Setup Spec

## Goal

Reduce the number of initial CLI commands a user must learn and run before they can successfully build and deploy an Automaton app.

The CLI should ask for missing information inside a small set of high-level commands instead of forcing the user through multiple low-level setup steps.

## Core Product Principle

Users should choose intent, not implementation steps.

Bad experience:

1. run `automaton config --server ...`
2. run `automaton login`
3. run `automaton whoami`
4. run `automaton create` or `automaton link`
5. run `automaton current-runner`
6. pick a connected runner from the list, or create/deploy one outside the CLI if none exists
7. run `automaton validate`
8. run `automaton deploy`

Desired experience:

1. run `automaton setup`
2. run `automaton new` or `automaton link`
3. run `automaton deploy`

Where possible, even step 2 should gather missing details interactively instead of requiring flags.

## Product Positioning

This spec defines the default local developer onboarding flow for Automaton apps.

It does not remove lower-level commands such as `login`, `config`, `whoami`, or `runners`. Those remain available for advanced users, debugging, automation, and scripting, but runner creation/bootstrap is not part of the CLI onboarding path.

The change is in the default UX:

- prefer a few high-level commands
- embed follow-up questions inside those commands
- treat lower-level commands as implementation details for the happy path

## Assumptions

1. The default hosted server is `https://automaton.monster`.
2. Most users should not need to type or configure the server explicitly.
3. Shared app-development guidance lives in `llms.txt` and linked `llms/` files.
4. The CLI may install local AI guidance files later, but this spec focuses first on reducing onboarding commands.

## Non-Goals

This spec does not:

- remove advanced or scriptable CLI commands
- define the final local AI agent-pack file layout
- replace `automaton deploy` or `automaton publish`
- define a local backend runtime for app execution
- require breaking changes to existing automation scripts

## Proposed Command Model

## Primary user-facing commands

### 1. `automaton setup`

Intent:

Prepare this machine or workspace for Automaton app development.

Responsibilities:

- default to `https://automaton.monster` automatically
- detect whether the user is already authenticated
- if not authenticated, run the login flow inside setup
- persist local CLI config needed for normal usage
- optionally detect whether the current folder is a multi-app workspace such as `/apps`
- optionally install local AI guidance files later if that feature exists
- finish in a state where the user can create, link, validate, and deploy apps

The user should not need to run `login`, `whoami`, or `config` separately on the happy path.

### 2. `automaton new`

Intent:

Create a new Automaton app linked to the current directory.

Responsibilities:

- create the remote app
- write `automaton.json`
- create `.automatonignore` if missing
- ask for app name if not provided
- optionally scaffold starter files later if that feature is enabled
- optionally detect whether the folder is an app root or part of a workspace

Examples:

```bash
automaton new
automaton new "My App"
```

The user should not need to understand the distinction between `create` and `init` in the happy path.

### 3. `automaton link`

Intent:

Attach the current folder to an existing remote app.

Responsibilities:

- write `automaton.json` for the existing app
- ask for app ID if not provided
- create `.automatonignore` if missing
- optionally offer to pull remote files if the folder is empty or incomplete

Examples:

```bash
automaton link
automaton link <app-id>
```

### 4. `automaton deploy`

Intent:

Validate and ship this app.

Responsibilities:

- run validation automatically or an equivalent internal preflight
- detect whether backend controllers exist
- if no backend exists, skip runner checks entirely
- if backend exists, verify runner assignment
- when a runner is missing, let the user choose from connected runners; if none exist, tell the user to create or deploy one outside the CLI
- only ask follow-up questions when the deploy actually needs them
- perform the upload and backend deploy

The user should not need to run `validate`, `current-runner`, or `runners` separately on the happy path. Runner creation or deployment stays outside the main CLI happy path.

## Secondary commands

These remain available but are not the default onboarding surface:

- `automaton login`
- `automaton logout`
- `automaton whoami`
- `automaton config`
- `automaton create`
- `automaton init`
- `automaton validate`
- `automaton current-runner`
- `automaton runners`

They should be treated as advanced, debugging, or automation commands.

## Interactive Prompting Rules

Missing information should be requested inside the high-level command that needs it.

### Setup prompts

`automaton setup` may ask:

- sign in now?
- open browser or paste token flow?
- is this a workspace root for multiple apps?
- optionally install local AI guidance here?

It should not ask for the server in the standard hosted path.

Server override belongs to advanced usage only, for example:

```bash
automaton setup --server https://custom-host
```

### New prompts

`automaton new` may ask:

- what is the app name?
- is this a single app folder or part of an apps workspace?
- optionally scaffold a starter app?
- if scaffolded later, which stack?

It should not require a separate create command followed by a separate linking step.

### Link prompts

`automaton link` may ask:

- what is the app ID?
- pull remote files now if the folder appears empty?

### Deploy prompts

`automaton deploy` may ask:

- backend detected; no runner assigned. Try automatic local runner setup now?
- destructive deploy requested; confirm `--clean`?

It should not ask questions unrelated to the current deploy attempt.

## Defaults

### Server default

For the standard hosted product, default to:

```text
https://automaton.monster
```

Users should only see server configuration when they intentionally choose advanced or self-hosted usage.

### Authentication default

If credentials are missing, `automaton setup`, `automaton new`, `automaton link`, and `automaton deploy` may prompt the user to authenticate rather than failing immediately.

### Backend runner default

If `backend/*.py` files do not exist:

- skip all runner setup logic
- do not surface runner commands

If backend controllers exist:

- check runner assignment only when needed
- prefer automatic repair when Docker and local setup allow it

## Recommended UX Flow

## First-time user, single app

```bash
automaton setup
automaton new "My App"
automaton deploy
```

Example behavior:

1. `setup` signs the user in and stores config.
2. `new` creates the app and writes `automaton.json`.
3. `deploy` validates, checks backend needs, and deploys.

## First-time user, existing app

```bash
automaton setup
automaton link <app-id>
automaton deploy
```

## Multi-app workspace

```bash
cd apps
automaton setup
```

Then inside an app folder:

```bash
automaton new "Calendar App"
```

Or:

```bash
automaton link <app-id>
```

## Future AI-guidance integration

If local AI guidance files are added later, they should be installed by `automaton setup` or `automaton new`, not as a separate required command.

The user should never need to remember a standalone "install prompts" command on the happy path.

## Command Semantics

## `automaton setup`

Suggested output shape:

- confirm default server selection
- confirm login success or existing login
- confirm config location used
- confirm optional workspace preparation
- end with one short next step

Example:

```text
Setup complete
- server: https://automaton.monster
- auth: signed in as razvan@example.com
- workspace: ready

Next step: run `automaton new "My App"` or `automaton link <app-id>`
```

## `automaton new`

Suggested output shape:

```text
App created
- name: My App
- appId: 1234...
- project link: automaton.json written

Next step: build your app locally, then run `automaton deploy`
```

## `automaton deploy`

Suggested behavior:

- perform internal validation first
- show short next-step guidance when blocked
- auto-fix only with user confirmation when the action has side effects

## Backwards Compatibility

Existing commands remain valid.

The new commands should be thin orchestrators over existing logic where possible:

- `setup` wraps or reuses login/config/whoami logic
- `new` wraps or reuses create/init/link logic
- `deploy` wraps or reuses validate/runner/deploy logic

This keeps implementation incremental and reduces risk.

## Recommended Implementation Order

## Phase 1

Implement `automaton setup`:

- default server
- embedded login flow
- ready-state output

## Phase 2

Implement `automaton new`:

- interactive app name prompt
- remote app creation
- local `automaton.json` creation
- `.automatonignore` creation

## Phase 3

Improve `automaton deploy` orchestration:

- internal preflight
- backend detection
- runner auto-fix prompt
- clearer blocked-state messaging

## Phase 4

Optionally add local AI-guidance installation into `setup` or `new`.

## Acceptance Criteria

This spec is successful when:

1. a first-time user can become productive with `automaton setup` without needing separate `config`, `login`, or `whoami` commands
2. a new app can be created with `automaton new` without needing separate create/init/link steps
3. backend-free apps can deploy without exposing runner commands
4. backend apps surface runner setup only when required
5. missing information is gathered inside the high-level command that needs it
6. the default hosted server does not need to be typed by the user
7. the happy-path command count for a new user is reduced to roughly three commands: `setup`, `new`, `deploy`

## Open Questions

1. Should `automaton new` also scaffold starter files in its first version, or should that remain a later enhancement?
2. Should `automaton deploy` automatically run the equivalent of `validate`, or keep `validate` as a visibly separate recommended step?
3. Should `setup` install local AI guidance files immediately, or ask first?
4. Should workspace-level and app-level setup be different commands, or different modes of `setup`?
5. Should `create` and `init` remain user-visible long term, or eventually become internal/advanced commands only?
