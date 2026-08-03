# Deployment Targets

Automaton separates the logical application from each deployed instance:

```text
WebApp (product identity and editable source)
└── AppDeploymentTarget (one deployed instance)
    └── Runner (execution location)
```

An app owns its name, description, publishing identity, authorization scope,
and source. A deployment target owns its Runner assignment, deployment status,
deployed component flags, timestamps, route, and cleanup state. A Runner is an
execution location; it may host several targets, including several targets of
the same app.

## Default and named targets

Every new app receives one server-owned target:

```text
Name: Default
Slug: default
IsDefault: true
```

The default keeps the normal workflow simple:

```bash
automaton deploy
```

Advanced users can create and deploy named targets:

```bash
automaton target list
automaton target add server-eu --runner infra-eu
automaton deploy server-eu
automaton target default server-eu
automaton target remove server-eu
```

Target definitions and Runner assignments live in the API database. The local
`automaton.json` identifies the logical app and does not contain a target map.
Omitting the target slug always resolves the current default on the server.

`--publish` and `--open` apply only to the default target. A named target must
already have an assigned Runner.

Studio keeps the default deployment as the primary app experience. The
**Deployment Targets** settings section exposes creation, editing, Runner
assignment, deployment, logs, default switching, and deletion when needed.

## Runtime identity and isolation

Executable Runner state is identified by both:

```text
WebAppId + DeploymentTargetId
```

Runner workspaces use:

```text
RUNNER_SCRIPTS_DIR/apps/{appId}/targets/{deploymentTargetId}/
├── source/
├── runtime/
├── venv/
└── versions/
```

Process keys, backend port allocation, restoration metadata, routes, logs,
actions, artifacts, and cleanup operations also include the deployment target
ID. Two targets can therefore use the same Runner without sharing runtime
content or process identity. Multiple Runners on one physical host remain
independent because targets refer to Runner identities, not servers.

Persistent application data remains app-scoped by design. It is not an
independently deployed runtime artifact and must not be confused with a target
workspace.

## Routing

The default target owns the app-specific private host and, when published, the
published app host:

```text
https://app-{appIdWithoutDashes}.apps.example/
https://{publishedSlug}.apps.example/
```

Named targets use the same app-specific private host with an explicit target
path:

```text
https://app-{appIdWithoutDashes}.apps.example/a/{appId}/t/{deploymentTargetId}/
```

The edge rewrites every route to the corresponding Runner endpoint:

```text
/apps/{appId}/targets/{deploymentTargetId}/
```

Switching the default changes which target receives the normal private and
published routes. It does not copy, delete, or otherwise mutate the old
target's runtime state.

## Status and lifecycle

Target status values are:

- `Unassigned`: no usable Runner assignment exists.
- `NotDeployed`: assigned but no successful runtime is recorded.
- `Deploying`: the target has an active deployment or lifecycle mutation.
- `Running`: the last target deployment succeeded.
- `Failed`: the last target deployment failed.
- `RunnerOffline`: the assigned Runner exists but is currently unavailable.

Runner-offline projection preserves deployment metadata. Deleting or revoking
a Runner clears the assignment and makes affected targets `Unassigned` while
also preserving deployment history.

Only one deployment or lifecycle mutation can claim a target at a time.
Different targets can be deployed concurrently. Completion callbacks are
accepted only for the app and target that own the active deployment.

Deleting or reassigning a deployed target requires target-specific Runner
cleanup. Cleanup is idempotent. `CleanupPending`, `LastCleanupAttemptAt`, and
`LastCleanupError` retain a recoverable failure when remote cleanup succeeds or
fails independently of the database operation.

## Runner authorization

Runner assignment and deployment use the central app Runner authorization
policy. Tags never grant access.

Normal Runners must be visible in the app's authorization context and must
advertise application-hosting capability. A privileged Runner additionally
requires all of the following:

- Runner purpose is `Infrastructure`.
- The app is the configured trusted infrastructure app.
- The caller is an administrator or system administrator.
- The Runner advertises application-hosting capability.

Revoking an Infrastructure Runner clears affected target assignments.

## Current limits

Deployment targets are independent instances, not replicas. Automaton does not
currently provide deploy-to-all, traffic splitting, rolling or canary
deployment, custom target domains, automatic failover, target-specific
secrets, environment inheritance, or target analytics.

Retries are explicit. There is no scheduler-driven cleanup or deployment retry
orchestration.
# Deployment targets and self-hosted Runners

Assign a Runner from an App's **Settings → Deployment Targets** editor. Standard self-hosted and Automaton-hosted Runners can be assigned normally. A Privileged Host Runner requires an explicit host-access acknowledgement because the App may access host files, processes, Docker, services, system state, and secrets.

The App keeps using its assigned Runner until you change the target. Privileged Host Runners are never selected automatically; they must always be explicitly assigned.
