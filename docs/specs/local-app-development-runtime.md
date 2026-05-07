# Local App Development Runtime Spec

## Goal

Make it possible for a user or AI agent to run, test, and debug an Automaton app locally before deployment, without changing the app programming model.

The local experience should preserve the existing Automaton app contract:

- frontend files live in the app workspace
- backend controllers live under `backend/`
- backend actions are invoked through `window.automatonApi.callController(controller, action, inputs)`
- Python controllers export `handle(action_name: str, inputs: dict) -> dict`
- action responses always use `{ success, data, error }`
- local persistence uses the same `data/` directory model as deployed apps

## Product Principle

One app model, multiple runtimes.

Users should not have to learn a second backend framework just to test locally.

Specifically:

- local development must not require rewriting controllers as FastAPI routes
- the production controller contract remains the source of truth
- local HTTP exists only as a browser-facing adapter for preview/testing
- container support is optional parity mode, not the default authoring path

## User Problem

Today, users can scaffold and deploy apps, but local app execution is not yet a first-class workflow.

This creates several problems:

1. frontend authors cannot quickly validate end-to-end behavior before publish
2. backend authors cannot easily debug controller failures locally
3. AI agents have no single local execution command to verify the app before deployment
4. users must infer too much about mocks, backend behavior, and deployment parity

## Desired User Experience

From the app folder, the user runs:

```bash
automaton app dev
```

The CLI should then:

1. detect the app root
2. validate the local app structure
3. start a local preview server
4. serve the frontend locally with file watching
5. expose a browser-facing local Automaton API bridge
6. execute Python controllers locally when the frontend calls them
7. print readable logs, errors, and timings in the terminal
8. provide one preview URL for the user to open in the browser

The user should be able to:

- open the app locally
- click through the UI
- submit forms
- see real backend behavior against local SQLite data
- switch to mock responses for frontend-only work
- inspect controller crashes and malformed responses immediately
- iterate before `deploy` or `publish`

## Non-Goals

This spec does not introduce:

- a new backend framework for app authors
- general-purpose hosting for arbitrary local web servers
- a new controller interface separate from `handle(action_name, inputs)`
- production publishing changes beyond optional reuse of validation logic
- remote debugging of published apps

## Core Commands

## 1. Local development server

```bash
automaton app dev
```

### Behavior

Starts the local app runtime in native mode.

Responsibilities:

1. detect linked app or local app folder
2. run preflight validation
3. choose available ports
4. serve frontend files
5. watch frontend/backend file changes
6. host a local HTTP endpoint for controller invocation
7. display logs and the preview URL

### Suggested output

```text
Automaton local app runtime
App: todo
Mode: native
Preview: http://localhost:3010
API bridge: http://localhost:3010/__automaton
Controllers: enabled
Mocks: fallback
Database: .\data\app.db
```

## 2. Validation

```bash
automaton app validate
```

### Behavior

Performs local app checks without starting the preview server.

Checks:

1. `automaton.json` exists when required
2. `index.html` exists
3. `backend/` structure is valid if present
4. controller files can be discovered
5. controller modules expose `handle`
6. controller return shape is valid or at least structurally testable
7. `backend/requirements.txt` presence is reported
8. `backend/mocks/` files are discoverable
9. `data/` directory can be created or written
10. Python runtime is available in native mode
11. container prerequisites are available in container mode

### Exit behavior

- exit code `0` when validation passes
- non-zero when blocking issues exist
- warnings for non-blocking issues such as missing mocks

## 3. Direct controller invocation

```bash
automaton app invoke todos list --input "{ \"page\": 1 }"
```

### Behavior

Runs a local controller action without starting the browser preview.

Useful for:

- backend debugging
- AI agent validation
- smoke testing controller actions
- fast verification of response shape

### Output

- pretty output by default
- `--json` for machine-readable result
- include Python traceback details on failure

## 4. Mock-first preview

```bash
automaton app dev --mock-only
```

### Behavior

Forces controller calls to resolve using `backend/mocks/<controller>.<action>.json`.

Use cases:

- frontend-only work
- UI review without Python installed
- deterministic demos

Blocking behavior:

- fail fast when a required mock file is missing

## 5. Container parity mode

```bash
automaton app dev --container
```

### Behavior

Runs the same local preview workflow, but executes the backend in a prepared containerized environment.

Use cases:

- dependency isolation
- OS-level package requirements
- closer deployment parity
- debugging issues that do not reproduce in native mode

## Runtime Architecture

## High-level model

The local runtime should be an Automaton-owned dev host, not a separate user-authored backend stack.

Request flow:

1. browser loads frontend from the local preview URL
2. frontend calls `window.automatonApi.callController('todos', 'list', inputs)`
3. local browser bridge sends a request to the CLI-hosted dev endpoint
4. the dev runtime resolves controller execution mode
5. controller is executed locally
6. result is normalized into `{ success, data, error }`
7. response returns to the browser
8. logs/timings/errors are printed in the CLI

## Local runtime components

### 1. Frontend host

Responsibilities:

- serve static app files
- support live reload or efficient refresh workflow
- inject or expose the local `window.automatonApi` bridge when needed
- preserve offline mock support semantics

### 2. Controller execution host

Responsibilities:

- discover controllers in `backend/*.py`
- execute `handle(action_name, inputs)` safely
- isolate execution per request or per worker process
- capture stdout/stderr
- normalize exceptions into readable CLI diagnostics

### 3. App data manager

Responsibilities:

- ensure `data/` exists
- preserve SQLite path conventions
- avoid accidental writes outside the app folder
- optionally reset local data for clean testing

### 4. Dev session logger

Responsibilities:

- show request start/finish
- show controller/action name
- show duration
- show warning/error state
- show tracebacks and malformed return-shape errors

## Native Mode vs Container Mode

## Native mode

Default mode for everyday development.

Advantages:

- fastest startup
- easiest edit-refresh loop
- simplest debugging
- lowest operational overhead

Requirements:

- Python available locally
- required packages installed locally or via app-managed environment

## Container mode

Optional parity mode.

Advantages:

- cleaner dependency isolation
- better consistency across machines
- better support for native dependencies

Tradeoffs:

- slower startup
- more moving parts
- Docker dependency

## Recommendation

Ship native mode first.

Add container mode only after the core local runtime is stable.

## Frontend Contract

The frontend contract should remain stable.

Apps should continue using:

```javascript
const result = await window.automatonApi.callController('todos', 'list', { page: 1 })
```

The local runtime must make this work in development without requiring users to redefine `window.automatonApi` manually.

### Local behavior rules

- if native backend execution is enabled, resolve through local controller execution
- if `--mock-only` is enabled, resolve only through mock files
- if mock fallback mode is enabled, use mocks only when the real controller is absent or disabled
- always return the standard Automaton response shape

## Backend Contract

The deployed controller contract remains unchanged.

Each controller:

- lives under `backend/`
- exports `handle(action_name: str, inputs: dict) -> dict`
- returns exactly:

```python
{"success": True, "data": data, "error": ""}
```

or:

```python
{"success": False, "data": None, "error": "description"}
```

## Error Handling Requirements

When controller execution fails locally, the runtime must distinguish between:

1. import error
2. missing `handle`
3. invalid action name
4. runtime exception inside action
5. malformed response shape
6. missing dependency
7. timeout
8. mock file not found

The CLI output should make the failure actionable.

Example:

```text
ERROR todos.list
Reason: controller raised exception
File: backend/todos.py
Message: no such table: todos
Duration: 18ms
```

## Logging Requirements

For every controller request, the CLI should show:

- timestamp
- controller.action
- duration
- outcome: success or failure
- optional request id

Optional verbose mode should show:

- input payload
- response payload
- Python stdout/stderr
- resolved execution mode

Suggested flags:

```bash
automaton app dev --verbose
automaton app invoke todos list --json
```

## File Watching and Reload

Local development must favor fast iteration.

### Frontend changes

- refresh or hot reload when feasible
- no manual server restart required for simple asset changes

### Backend changes

- detect controller file changes
- reload safely before next request
- avoid requiring manual runtime restart for normal Python edits

### Configuration changes

- restart may be required for deeper environment/runtime changes
- CLI should state clearly when restart is needed

## Python Environment Strategy

The runtime must support a predictable Python execution strategy.

Recommended order:

1. app-local virtual environment if present
2. configured Python path from CLI settings if present
3. system Python fallback

Validation should clearly report:

- selected Python executable
- missing dependencies
- whether `backend/requirements.txt` exists

Future enhancement:

- `automaton app install-deps` to create or update a local venv from `backend/requirements.txt`

## Security and Safety

Local execution runs arbitrary app code on the user machine.

Therefore:

- the CLI must make it explicit that local controllers execute local Python code
- destructive cleanup commands must require confirmation unless `--yes` is passed
- filesystem access should be limited by convention to the app workspace, but code execution is ultimately trusted local code
- no hidden remote execution should occur during local preview

## Proposed Flags

For `automaton app dev`:

```bash
--port <n>
--host <host>
--open
--mock-only
--mock-fallback
--container
--verbose
--no-browser
```

For `automaton app validate`:

```bash
--container
--json
--strict
```

For `automaton app invoke`:

```bash
--input <json>
--json
--timeout <seconds>
--mock-only
```

## Implementation Phases

## Phase 1: Validation and direct invoke

Deliver:

- `automaton app validate`
- `automaton app invoke`
- controller discovery
- Python execution adapter
- response shape validation

Why first:

- smallest useful slice
- immediately useful for backend debugging and AI validation
- proves controller execution model before preview-server work

## Phase 2: Native preview runtime

Deliver:

- `automaton app dev`
- local frontend host
- browser-facing Automaton API bridge
- request logging
- file watching and backend reload

Why second:

- completes the main local testing workflow
- keeps the initial slice narrow and testable

## Phase 3: Mock modes and UX polish

Deliver:

- `--mock-only`
- `--mock-fallback`
- clearer diagnostics
- browser auto-open
- improved structured logs

## Phase 4: Python environment support

Deliver:

- local venv detection
- dependency install helper
- clearer dependency validation and repair guidance

## Phase 5: Container parity mode

Deliver:

- `automaton app dev --container`
- Docker prerequisite checks
- parity-oriented runtime configuration

## Acceptance Criteria

This feature is complete when:

1. a user can run `automaton app dev` from an app folder and open a working local preview
2. frontend calls to `window.automatonApi.callController(...)` execute local controllers without app code changes
3. local controller responses match deployed response semantics
4. malformed controller behavior is surfaced with actionable diagnostics
5. `automaton app validate` catches obvious structural/runtime issues before deploy
6. `automaton app invoke` can execute controller actions directly for debugging and automation
7. mock-only mode supports frontend work without live Python execution
8. the runtime is usable by both humans and AI agents through stable CLI output

## Open Questions

1. Should the frontend host be implemented directly inside the CLI, or should the CLI launch a small bundled dev host process?
2. How much browser live-reload sophistication is actually needed for the first release?
3. Should local preview require `automaton.json`, or allow completely local unlinked apps?
4. Should Python execution happen in-process, through a subprocess per call, or through a reusable worker process?
5. Do we want `automaton app reset-data` in the first release, or later?
6. Should mock fallback be enabled by default, or only when requested explicitly?
7. How much of this runtime should later be reused inside Studio preview or app testing features?
