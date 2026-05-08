# Autonomous AI End-to-End App Flow

## Goal

Make it realistic for an AI coding agent to complete the full Automaton app lifecycle with minimal human intervention:

1. install or verify the CLI
2. authenticate
3. create or link an app
4. scaffold the app locally
5. provision or assign a runner when backend code exists
6. deploy frontend and backend
7. publish after explicit user confirmation
8. validate the published app

The primary automation surface should be the CLI.

MCP should be a secondary, optional interface that exposes the same core capabilities in a more structured form.

## Product Positioning

### Principle

The AI should not need MCP in order to succeed.

The minimum viable autonomous workflow should work with:

- local file access
- terminal access
- Automaton CLI
- a logged-in or otherwise authenticated CLI session

### Why this split matters

`llms.txt` teaches the model what to do.

The CLI lets the model actually do it.

MCP improves reliability and tool ergonomics, but should not be the only execution path.

### Recommended architecture

1. `llms.txt` and linked files define one canonical full-flow runbook
2. CLI supports the entire runbook end to end
3. MCP mirrors the same operations with structured IO

## Target User Experience

### User prompt

The user says something like:

> Build me a TODO app with SQLite persistence and deploy it to Automaton.

### AI outcome

The AI should be able to:

1. ensure the CLI exists
2. authenticate or resume an authenticated CLI session
3. create the remote app and write `automaton.json`
4. scaffold the frontend and backend locally
5. ensure a runner exists for backend controllers
6. deploy the app
7. ask once before publish
8. publish and validate the live URL

### Acceptable human interaction

The autonomous flow may still ask the user for:

1. login approval if interactive authentication is required
2. confirmation before making the app publicly available
3. confirmation before provisioning a local Docker runner if that affects the machine

The autonomous flow should not require the user to manually look up app ids, runner ids, or file structure rules.

## Current Gaps

Based on the current `llms.txt` guidance and the recent real-world TODO app exercise, the main gaps are not app generation. The main gaps are execution reliability.

### Current strengths

- the LLM docs already teach app structure reasonably well
- the CLI can already configure server and key, initialize links, deploy, publish, list runners, and assign a runner
- backend app deployment is already viable when a runner exists

### Current weaknesses

- login is still API-key-centric instead of agent-friendly session auth
- app creation and linking are not yet expressed as one obvious autonomous flow primitive
- runner provisioning is still external knowledge, not a first-class CLI capability
- the CLI lacks a single preflight validation command
- the CLI output is not yet optimized as a stable machine interface across the full flow
- `llms.txt` still reads as guidance, not as a strict executable workflow with recovery branches

## CLI-First Capability Plan

## Required CLI Commands

The CLI should be sufficient for the entire autonomous flow.

### 1. Authentication

Add:

```bash
automaton login
automaton whoami
automaton logout
```

#### Behavior

- `automaton login` should support a browser or device-code style login flow
- after login, the CLI should persist a machine-usable session locally
- `automaton whoami` should return the authenticated user, server, and active auth mode
- `automaton logout` should clear local session state

#### Why this matters

This removes the need to force the user to paste API keys into an AI conversation just to unlock the rest of the flow.

### 2. App creation and linking

Add:

```bash
automaton create --name "My App"
automaton link <app-id>
automaton status
```

#### Behavior

- `automaton create` creates the remote app and writes `automaton.json`
- `automaton create` should also create `.automatonignore` if missing
- `automaton status` should show linked app id, server, deploy dir, build command, runner assignment, and publish status

#### Why this matters

The AI should not have to infer when to use `init` versus `link` versus manual file writing.

### 3. Runner discovery and provisioning

Keep and improve:

```bash
automaton runners
automaton set-runner <runner-id>
automaton current-runner
```

#### Behavior

- `automaton runners` should list runner ids and enough health/status info for autonomous selection
- `automaton current-runner` should show the runner currently assigned to the linked app
- if no suitable connected runner exists, the CLI should clearly tell the user to create or deploy one in Automaton Studio before backend deploy

#### Why this matters

Backend execution without an existing runner is the real blocker. The CLI should help users choose an existing connected runner, not provision infrastructure for them.

### 4. Validation and preflight

Add:

```bash
automaton validate
```

#### Behavior

The command should validate:

1. CLI is authenticated
2. `automaton.json` exists and is valid
3. deploy directory exists if configured
4. build command exists if configured
5. backend controllers exist or do not exist
6. a runner is assigned when backend controllers exist
7. the linked app exists remotely

#### Why this matters

The AI needs one cheap discriminating check before deploy, instead of piecing state together across multiple commands and files.

### 5. Deploy and publish

Keep and improve:

```bash
automaton deploy
automaton publish
```

Add:

```bash
automaton publish --yes
automaton published-url
automaton open
```

#### Behavior

- `automaton publish --yes` should support a non-interactive publish after the AI has already obtained user approval
- `automaton published-url` should return the public URL directly
- `automaton open` should open the local preview or published URL, depending on state and flags

#### Why this matters

Agents need a clean way to finish the flow and validate the actual result.

### 6. Machine-friendly output

Every important CLI command should support:

```bash
--json
```

At minimum:

- `automaton whoami --json`
- `automaton status --json`
- `automaton runners --json`
- `automaton current-runner --json`
- `automaton validate --json`
- `automaton deploy --json`
- `automaton publish --json`
- `automaton published-url --json`

#### Why this matters

AI agents are much more reliable when they can parse stable structured output instead of scraping human-readable terminal text.

## Server/API Capability Plan

The CLI needs a stable server-side contract behind it.

### 1. Session authentication suitable for CLI login

Required capability:

- browser or device-code style login flow for CLI use
- token or session persistence usable by the CLI without exposing secrets back through the model

This is the key change that makes "login once, then let the AI continue" realistic.

### 2. App creation endpoint optimized for CLI

Required behavior:

- create app
- return app id, name, slug, defaults, and server metadata in one response
- support CLI-first initialization without forcing the agent through Studio UI assumptions

### 3. Runner provisioning support

Required behavior:

- server-side acceptance path for a newly bootstrapped local runner
- predictable status reporting so the CLI can guide runner selection clearly before backend deploy

### 4. Validation endpoints or validation-ready existing endpoints

Required behavior:

- the CLI must be able to verify linked app existence, runner assignment, backend presence, and publish readiness without fragile heuristics

### 5. Publish-safe semantics

Required behavior:

- publish endpoint should support an explicit confirmation flag or a non-interactive CLI path once approval was already gathered from the user

## MCP Role

MCP should be implemented as a secondary enhancement over the same system.

### MCP should expose

1. create app
2. read app status
3. list runners
4. assign runner
5. ensure local runner
6. upload files
7. deploy backend
8. publish app
9. read published URL

### MCP should not be the only path for

- login
- app creation
- runner provisioning
- deploy
- publish

If the CLI cannot do those reliably, the platform is not yet ready for broad autonomous AI use.

## `llms.txt` Changes

## Objective

Move from descriptive documentation to a canonical autonomous runbook.

### Add a new section

Add a section like:

## Autonomous Full Flow

When the AI is asked to build an Automaton app from scratch locally, follow this order:

1. Ensure the Automaton CLI is installed.
2. Run `automaton whoami`.
3. If not authenticated, run `automaton login` and wait for completion.
4. Check for `automaton.json` in the project root.
5. If missing, run `automaton create --name "<app-name>"`.
6. Scaffold the app locally using the simplest suitable frontend stack.
7. If Python backend controllers exist, run `automaton runners`.
8. If no suitable connected runner exists, stop and tell the user to create or deploy one in Automaton Studio.
9. Run `automaton validate`.
10. Run `automaton deploy`.
11. Ask for confirmation before public publish.
12. After approval, run `automaton publish --yes`.
13. Run `automaton published-url` and validate the live app.

### Add recovery rules

The same section should also define what to do when:

1. CLI is missing
2. login fails
3. no runner is available
4. backend deploy is skipped due to missing runner
5. publish is blocked pending confirmation

### Add stronger completion criteria

The AI should not consider the job done until:

1. `automaton.json` exists
2. app files exist locally
3. runner assignment exists when backend controllers exist
4. deploy succeeded
5. publish succeeded if the user asked for a live public app

## Concrete CLI Flow

This is the canonical terminal sequence the AI should eventually be able to run without improvisation.

```bash
automaton whoami --json
automaton create --name "My App" --json
automaton validate --json
automaton runners --json
automaton set-runner <runner-id> --json
automaton deploy --json
automaton publish --yes --json
automaton published-url --json
```

## Proposed Phases

### Phase 1: CLI Authentication + App Creation

Deliver:

- `automaton login`
- `automaton whoami`
- `automaton logout`
- `automaton create`
- `automaton status`
- JSON output for status and auth commands

Success criteria:

- an AI can authenticate and create a linked app without asking for a raw API key

### Phase 2: Validation + Better Runner UX

Deliver:

- `automaton validate`
- `automaton current-runner`
- improved `automaton runners --json`

Success criteria:

- an AI can determine whether deploy is possible before attempting it

### Phase 3: Local Runner Provisioning

Deliver:

- better runner selection guidance and early failure when no connected runner exists

Success criteria:

- an AI can clearly determine when a connected runner must be created or deployed outside the CLI

### Phase 4: Publish + Validation Finishers

Deliver:

- `automaton publish --yes`
- `automaton published-url`
- `automaton open`
- JSON output for deploy and publish commands

Success criteria:

- an AI can complete the full local-to-live flow with only one final approval step before publish

### Phase 5: MCP Mirror Layer

Deliver:

- MCP tools for the same operations already supported by the CLI

Success criteria:

- MCP-enabled agents can execute the same flow more cleanly, but the CLI remains fully capable on its own

## Recommended Acceptance Test

The feature should be considered successful when a coding agent can complete this scenario:

1. fresh local folder
2. no `automaton.json`
3. no pre-existing linked app
4. backend controllers included
5. user authenticates once via CLI login
6. AI creates the app
7. AI provisions or selects a runner
8. AI deploys
9. AI asks once before publish
10. AI publishes
11. AI validates the public URL

## Non-Goals

This proposal does not require:

- removing API-key support
- making MCP mandatory
- replacing the Studio App Editor workflow
- fully eliminating all user confirmations

## Recommendation

Build this in the following order:

1. strengthen `llms.txt` into a strict autonomous runbook
2. make the CLI capable of the entire runbook without MCP
3. expose the same capabilities via MCP as a better structured path

That sequence gives the best platform outcome:

- any strong coding agent with terminal access can succeed
- MCP-enabled agents gain better reliability and ergonomics
- the product does not become dependent on one integration style