---
title: Environments and Runner relationships
order: 15
---

# Environments and Runner relationships

Use this page when an administrator needs to understand how an app, its deployed instances, and Runner infrastructure relate to one another.

## The four concepts

- **App** — the logical product: source code, name, description, ownership, publishing settings, and shared configuration.
- **Environment** — one independently running copy of an app (stored internally as a deployment target).
- **Runner** — the execution service assigned to a deployment target.
- **Runner Host** — the physical or virtual server that may run one or more Runners.

An app can have several targets:

```text
App
├── Live target → Runner A
├── Staging target → Runner A
└── EU target      → Runner B
```

Each target owns its Runner assignment, deployed files, backend process and port, logs, deployment state, and private runtime route. Targets can share a Runner without sharing runtime state.

## Live and additional environments

Every app has one primary target, shown as **Live**. Live is selected when a deployment does not specify a target:

```bash
automaton deploy
```

Named environments are optional and are selected by slug:

```bash
automaton deploy staging
```

The Live target receives the app's normal private URL and, when published, its public URL. An additional target has its own private runtime route. Promoting a target to Live changes which running copy receives the normal URLs; it does not copy or delete another target's runtime.

## Where users manage environments

Users manage environments from **Apps → open an app → Settings → Environments**. The page separates **Live** from **Additional environments**. They can deploy an environment, open its preview, view logs, edit its name or slug and runtime destination, promote an additional environment to Live, and delete an additional environment.

The normal destination label is **Automaton Hosted**, **Self-hosted · Standard**, or **Self-hosted · Privileged Host**. Studio does not expose the identity of an Automaton-managed Hosted Runner in the normal destination selector. An **Advanced** section shows runner and target IDs, runtime transport, and the internal preview URL for troubleshooting.

Promoting asks for confirmation because it changes which deployed target is served through the App's normal private and published URLs. It does not redeploy or reassign either target. The Live target cannot be deleted until another target is promoted.

Most users can ignore additional targets and continue using Live.

## Environment rollout and migration checklist

Normal users work with **Environments**; administrators may use the underlying
deployment-target terminology when diagnosing IDs, routes, and Runner state.
The shared source is deployed into independent environments:

```text
App → Environment → Runtime destination
```

Before a migration or rollout, inventory Apps with multiple primary targets and
no primary target. Preserve every assigned, deployed, Live, or historically
referenced target. Never delete target rows manually.

For a release that changes target-aware SignalR methods, deploy API and Studio
together and ask users to refresh existing Studio tabs. Verify one-environment
deploy/publish, multi-environment preview and logs, Live promotion without
redeployment, and Privileged Host approval state. Administrators grant or revoke
approval on the target's Privileged Host access action; assignment remains
blocked while approval is absent. If rollback is required,
restore the previous API and Studio revision without changing the database or
deployed environment rows, then repeat the route, log, and publish smoke checks.

## Where administrators manage Runner infrastructure

Use **Admin → Runners → Runner Hosts** for Infrastructure Runners and the servers they control.

Use **Admin → Hosted Runners → Runner Containers** for managed tenant Runner containers. A Hosted Runner container is not a Deployment Target: the container may execute several targets from different apps, while each target remains isolated by app and target identity.

## Target statuses

Private app links use `app-{appId:N}.{configured apps domain}`, with no hyphens in the app ID. The primary target opens at `/` on that host. Named targets open at `/a/{appId}/t/{targetId}/` on the same host. The API's `RoutePath` is relative to the app host, not the shared apps domain. Use Studio's Open or Copy URL action instead of constructing a `/private/{appId}` link. No legacy path redirect is provided.

- **Unassigned** — no Runner is assigned.
- **Not deployed** — a Runner is assigned, but no successful deployment is recorded.
- **Deploying** — a deployment or protected lifecycle operation is active.
- **Running** — the latest deployment succeeded.
- **Failed** — the latest deployment failed.
- **Runner offline** — deployment history exists, but the assigned Runner is unavailable.

Changing a deployed target's Runner requires confirmation because the selected target's existing runtime must be cleaned up. Sibling targets are not removed. The Live target cannot be deleted until another target is promoted to Live.

## Operational rule

Do not use the Hosted Runner Container actions to manage an Infrastructure Runner. Repair or replace an Infrastructure Runner from **Admin → Runners → Runner Hosts**, then use Hosted Runner reprovisioning for the managed tenant container.
