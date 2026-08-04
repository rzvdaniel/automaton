---
title: Deployment targets and Runner relationships
order: 15
---

# Deployment targets and Runner relationships

Use this page when an administrator needs to understand how an app, its deployed instances, and Runner infrastructure relate to one another.

## The four concepts

- **App** — the logical product: source code, name, description, ownership, publishing settings, and shared configuration.
- **Deployment target** — one independently running copy of an app.
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

## Live and additional targets

Every app has one primary target, shown as **Live**. Live is selected when a deployment does not specify a target:

```bash
automaton deploy
```

Named targets are optional and are selected by slug:

```bash
automaton deploy staging
```

The Live target receives the app's normal private URL and, when published, its public URL. An additional target has its own private runtime route. Promoting a target to Live changes which running copy receives the normal URLs; it does not copy or delete another target's runtime.

## Where users manage targets

Users manage targets from **Apps → open an app → Settings → Deployment Targets**. They can view target status and Runner assignment, deploy a target, open its runtime, view logs, edit its name or slug, change its Runner, promote it to Live, and delete an additional target.

Most users can ignore additional targets and continue using Live.

## Where administrators manage Runner infrastructure

Use **Admin → Runners → Runner Hosts** for Infrastructure Runners and the servers they control.

Use **Admin → Hosted Runners → Runner Containers** for managed tenant Runner containers. A Hosted Runner container is not a Deployment Target: the container may execute several targets from different apps, while each target remains isolated by app and target identity.

## Target statuses

- **Unassigned** — no Runner is assigned.
- **Not deployed** — a Runner is assigned, but no successful deployment is recorded.
- **Deploying** — a deployment or protected lifecycle operation is active.
- **Running** — the latest deployment succeeded.
- **Failed** — the latest deployment failed.
- **Runner offline** — deployment history exists, but the assigned Runner is unavailable.

Changing a deployed target's Runner requires confirmation because the selected target's existing runtime must be cleaned up. Sibling targets are not removed. The Live target cannot be deleted until another target is promoted to Live.

## Operational rule

Do not use the Hosted Runner Container actions to manage an Infrastructure Runner. Repair or replace an Infrastructure Runner from **Admin → Runners → Runner Hosts**, then use Hosted Runner reprovisioning for the managed tenant container.
