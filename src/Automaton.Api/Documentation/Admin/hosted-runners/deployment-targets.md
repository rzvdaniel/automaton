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

The normal destination label is **Automaton Hosted**, **Self-hosted · Standard**, **Self-hosted · Machine Management**, or **Self-hosted · Privileged Host**. Machine Management uses the machine's Docker socket through Automaton's typed app-container operations; web apps and isolated Actions run in bounded containers. Raw workflows and custom-step code remain disabled on this profile because those legacy handlers execute in the management process. Its installation creates a private Docker bridge and passes that bridge's validated gateway address to the Runner so it can reach published app-container ports; routed HTTP continues through the existing API runtime tunnel. Treat the management Runner as trusted infrastructure because a compromise could control host Docker. Its workspace and app data share an installation-scoped Docker volume; this profile does not enforce per-app disk quotas. Studio does not expose the identity of an Automaton-managed Hosted Runner in the normal destination selector. An **Advanced** section shows runner and target IDs, runtime transport, and the internal preview URL for troubleshooting.

To install a Machine Management Runner, open **Runners → Install Self-Hosted Runner** in Studio and prepare an Ubuntu host with Docker Engine installed and running. The generated command checks Ubuntu and Docker availability before pulling the Runner image. ext4 is the tested storage baseline, but working Docker storage on XFS or another supported filesystem is accepted. The command does not install Docker, move existing Docker data, or configure the filesystem.

After assigning a target to a Machine Management Runner, deploy it with the CLI (`automaton deploy` for Live or `automaton deploy <environment>` for a named target). The Runner builds source packages in a bounded container or pulls the declared runtime image, starts a candidate container, and waits for its configured HTTP readiness check before the target is marked deployed and its route is activated. A failed build or unhealthy replacement leaves the previous healthy runtime serving. App data is stored separately from replaceable source/runtime artifacts and survives updates and Runner restarts. Python Action dependencies are prepared in a bounded container, and Actions execute in bounded containers; dependency installation does not run on the management host.

Build, app, Compose, Action, and Action dependency containers on a Machine Management Runner receive server-owned CPU, memory, and PID limits from the API's `MachineContainerResources` settings. `MachineContainerResources:MinimumFreeDiskMb` defaults to 1024 MB (minimum 256 MB) and refuses new deployments, container work, Actions, and static publications when either the configured workspace filesystem or Runner storage filesystem is below that reserve. A refused deployment removes its staging data and leaves the current runtime in place. This is a free-space admission check, not a hard disk quota; it does not provide aggregate reservations or per-app/per-user disk limits. The existing deployment dispatcher and Runner Action queue also bound concurrent deployments and jobs.

Promoting asks for confirmation because it changes which deployed target is served through the App's normal private and published URLs. It does not redeploy or reassign either target. The Live target cannot be deleted until another target is promoted.

Most users can ignore additional targets and continue using Live.

Studio's app Overview reports the current Live runtime separately from deployment history. Its **Last successful deployment** timestamp can remain after runtime cleanup or Runner reassignment and does not, by itself, mean the app is currently deployed.

## Deleting a whole app

**Apps > open an app > Settings > Danger Zone > Delete App** permanently removes
its assigned runtimes, app workspace, deployment staging, and persistent app data.
Every assigned Runner must be online, including assignments without a currently
running web runtime. Cleanup discovers older owned containers as well as the
current runtime. The shared account volume and other apps are preserved.

Deleting an additional environment still preserves the app's shared persistent
data. Whole-app deletion waits for all target cleanups before deleting that data.
A persisted app deletion claim blocks creation of new environments and deployment
admission throughout Runner cleanup, source cleanup, and app-record deletion.
The claim has no automatic expiry: an API interruption leaves admission blocked.
If cleanup fails, the app record is retained: resolve Runner availability or the
reported cleanup problem, then retry **Delete App**. Already removed runtimes and
data are not restored.

Apply the app deletion-claim migration and update every API replica. Drain old API
slots before exercising deletion; older API versions do not honor this claim.

Release matching API, Hosted Runner, and Infrastructure Runner versions for this
cleanup contract. Older Runners cannot perform the new container-discovery
operation; do not rely on them to complete deletion.

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

Static sites declare a build command and output directory with no runtime command;
a Machine Management Runner publishes their output through one shared static
serving container per machine. Successful static and supported Compose deployments
retain HTTP routing state even though they do not declare a runtime command.
Actions-only deployments have no HTTP route. After upgrading from a release that
omitted static or Compose routing state, redeploy affected environments to restore
their routes; publishing alone does not repair that deployment state.

For a browser site calling an API on another app hostname, configure CORS in the
API app for the website origin, allowed methods, and headers. The runtime tunnel
forwards preflight `OPTIONS` requests and the app's response; Automaton's control
API CORS policy does not authorize cross-origin requests for user apps.

Saving a change to the App's `RequiresAuthentication` setting refreshes routes for its deployed targets, including Hosted and BYO Runner routes. A redeployment is not required to apply or remove runtime authentication. If saving fails after database persistence, resolve the reported failure and retry the same setting: every authorized save retries route application. Until route application succeeds, the previous route may remain active. Runtime visibility and authentication are separate settings; a private URL alone does not require sign-in.

Private app links use `app-{appId:N}.{configured apps domain}`, with no hyphens in the app ID. The primary target opens at `/` on that host. Named targets open at `/a/{appId}/t/{targetId}/` on the same host. The API's `RoutePath` is relative to the app host, not the shared apps domain. Use Studio's Open or Copy URL action instead of constructing a `/private/{appId}` link. No legacy path redirect is provided.

- **Unassigned** — no Runner is assigned.
- **Not deployed** — a Runner is assigned, but no successful deployment is recorded.
- **Deploying** — a deployment or protected lifecycle operation is active.
- **Running** — the latest deployment succeeded.
- **Failed** — the latest deployment failed.
- **Runner offline** — deployment history exists, but the assigned Runner is unavailable.

Changing a deployed target's Runner requires confirmation because the selected target's existing runtime must be cleaned up. Sibling targets are not removed. The Live target cannot be deleted until another target is promoted to Live.

## Operational rule

New workloads should use an explicitly assigned Machine Management Runner. The legacy **Runner Containers** view may be used for read-only historical inspection of existing Hosted Runner records; it cannot create or reprovision per-user Runner containers. Repair an Infrastructure Runner from **Admin → Runners → Runner Hosts**.

A failed package/runtime application, or a result accepted by no Runner, is a failed CLI deployment with a nonzero exit code. Inspect the reported failure and target/Runner logs before retrying. A failed replacement can leave the previous healthy runtime serving; that availability does not make the attempted deployment successful. Restore-point warnings remain distinct from runtime failure.

## Recover an interrupted whole-app deletion

A whole-app deletion fences new targets and deployments until cleanup finishes. An active deletion or a retained interrupted claim returns HTTP 409; normal deletion does not silently replace another owner's claim.

For an interrupted deletion, an administrator who also has the existing app-deletion permission may explicitly retry `DELETE /api/Apps/{appId}?recoverInterrupted=true`. There is no separate Studio recovery control. This is a destructive retry of the original deletion, including assigned runtime and app-storage cleanup.

If an assigned Runner is permanently gone, an administrator may use `DELETE /api/Apps/{appId}?recoverInterrupted=true&abandonOfflineRuntimes=true` after verifying on the Runner host that the app's runtime containers and storage have already been removed. This option requires an existing interrupted deletion claim. It skips cleanup commands only for offline Runners, deletes the app record and API-side files, and records the abandonment in the deletion audit event. An offline Runner that still has app data will not be cleaned by this request. The endpoint accepts a signed-in administrator or that administrator's Developer API key; app ownership and project restrictions still apply.

Before requesting recovery, confirm that the original deletion executor has stopped. During the first upgrade to this recovery version, stop all older API deletion executors before recovering their retained claims: older versions do not hold the new executor lock. Do not run an older API alongside recovery requests. Confirm that no deployment is active. For normal recovery, every assigned Runner needed for cleanup must be online. Project membership and app ownership restrictions still apply; administrator status does not override them.

The MySQL session lock rejects concurrent deletion executors and releases when their session ends. Recovery replaces the interrupted app claim with a new owner token, then resumes idempotent target/app cleanup. Targets with an active deployment operation or execution attempt remain conflicts. If recovery fails, the deletion intent continues blocking new admission; correct the reported cleanup problem and repeat the same supported recovery request. Do not clear claims or deployment state through database writes. Successful deletion returns 204; confirm the app is absent afterward.
