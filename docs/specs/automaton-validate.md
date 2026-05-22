# Automaton Validate Command Spec

> Historical spec note:
> This file describes validate-command design and may use older shorthand such as local `backend/`.
> For the current source/live storage contract, prefer:
> - `AGENTS.md`
> - `docs/dev/app-source-runtime-split.md`
> - `docs/dev/app-deploy-structure.md`
> - `src/automaton-vue/src/content/docs/automaton-cli.md`

## Goal

Define a narrow, implementable scope for `automaton validate` as a cheap preflight check before `automaton deploy`.

The command should answer one question quickly:

Is this project structurally ready to deploy to Automaton Studio?

It should not upload files, mutate remote state, or perform a deploy.

## Product Positioning

`automaton validate` is the low-cost safety check in the current app workflow.

Current intended workflow:

1. build or edit locally
2. run `automaton validate`
3. run `automaton deploy`
4. preview in Automaton Studio
5. inspect logs in Automaton Studio
6. publish only after approval

## Non-Goals

This command does not:

- deploy files
- publish the app
- execute backend controllers locally
- replace Studio preview/log inspection
- provision a runner automatically

It may recommend follow-up commands, but should not perform them unless future flags explicitly allow it.

## Command Surface

```bash
automaton validate
automaton validate --json
automaton validate --strict
```

## Core Responsibilities

The first version should validate only the highest-value preconditions for deploy.

### 1. App linkage

Checks:

- `automaton.json` exists in the project root
- `appId` is present
- `name` is present when required
- `server` is resolvable from environment, `automaton.json`, or CLI config

Failure examples:

- missing `automaton.json`
- missing `appId`
- invalid or empty server value

### 2. Frontend deploy shape

Checks:

- `frontend.deployDir` is present for build-based frontend apps
- the resolved frontend deploy directory exists
- `index.html` exists in the resolved frontend deploy directory
- if `frontend.build` is configured, the command string is present and non-empty

Failure examples:

- configured `frontend.deployDir` does not exist
- no deployable frontend entry file found

### 3. Backend presence and runner requirement

Checks:

- whether `backend/*.py` files exist
- whether backend deployment is expected
- whether a runner is assigned when backend controllers exist

Failure examples:

- backend controllers exist but no runner is assigned

### 4. Remote app existence

Checks:

- linked app id resolves remotely
- the current identity can access that app

Failure examples:

- remote app not found
- unauthorized access

## Optional Early Checks

These are useful, but can ship after the first version if needed:

- verify `.automatonignore` exists or warn if missing
- verify `backend/requirements.txt` exists when imports imply extra packages
- verify known paths referenced by config are valid
- verify a configured build command executable likely exists

## Output Design

## Human-readable output

Default output should be short and actionable.

Example success:

```text
Validation passed
- app link: ok
- frontend deploy shape: ok
- backend runner: ok
- remote app access: ok
```

Example failure:

```text
Validation failed
- app link: ok
- frontend deploy shape: ok
- backend runner: missing runner assignment
- remote app access: skipped

Next step: run `automaton runners` and `automaton set-runner <runner-id>`
```

## JSON output

`--json` should be stable enough for AI agents and scripts.

Suggested shape:

```json
{
  "success": false,
  "summary": "Validation failed",
  "checks": [
    { "name": "appLink", "success": true, "error": "" },
    { "name": "frontendDeployShape", "success": true, "error": "" },
    { "name": "backendRunner", "success": false, "error": "No runner assigned for backend app" },
    { "name": "remoteAppAccess", "success": false, "error": "Skipped because a blocking check failed" }
  ],
  "nextSteps": [
    "Run automaton runners",
    "Run automaton set-runner <runner-id>",
    "Run automaton validate again"
  ]
}
```

## Strict Mode

`--strict` upgrades warnings into failures where appropriate.

Candidate strict-mode warnings:

- missing `.automatonignore`
- missing optional metadata in `automaton.json`
- backend directory layout that is unusual but not blocking

## Failure Semantics

Exit codes:

- `0` when all required checks pass
- non-zero when any blocking check fails

Blocking failures should stop on:

- missing or invalid app linkage
- missing deployable frontend shape
- missing runner for backend app
- remote app inaccessible

## Recommended Implementation Order

## Phase 1

Implement:

- app linkage validation
- frontend deploy shape validation
- backend runner requirement validation
- remote app existence/access check
- short human-readable output

## Phase 2

Implement:

- `--json`
- `--strict`
- clearer next-step suggestions

## Acceptance Criteria

This command is complete for the first release when:

1. it detects missing or invalid `automaton.json`
2. it detects missing deployable frontend structure
3. it detects backend apps without runner assignment
4. it detects inaccessible or missing remote apps
5. it exits non-zero on blocking failures
6. its output is clear enough for both humans and AI agents to choose the next command

## Open Questions

1. Should `automaton validate` ever run `frontend.build`, or only validate static preconditions?
2. Should the command fail when `.automatonignore` is missing, or only warn?
3. Should remote app checks happen only after all local checks pass, or always run when possible?
4. How stable does the JSON contract need to be in the first version for agent use?
