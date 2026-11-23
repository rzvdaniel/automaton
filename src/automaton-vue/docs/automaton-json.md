---
title: automaton.json reference
description: Reference for the Automaton app identity and generic build, runtime, health, and storage contract.
order: 130
---

# automaton.json reference

`automaton.json` lives at the project root. It links the project to an Automaton
app and declares the application contract.

## Generic application shape

This is the general shape. For a static site, `automaton new NAME --template static` writes a working one ([Build an Automaton app](./build-an-automaton-app#static-site)); its shape is the reference, so prefer it to hand-written variants.

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My App",
  "summary": "One line for the store card",
  "application": {
    "build": {
      "image": "runtime-image:tag",
      "command": {
        "executable": "build-tool",
        "arguments": ["build"],
        "workingDirectory": ".",
        "environment": {}
      },
      "outputDirectory": "dist",
      "timeoutSeconds": 900
    },
    "runtime": {
      "image": "runtime-image:tag",
      "command": {
        "executable": "app-server",
        "arguments": [],
        "workingDirectory": ".",
        "environment": {}
      },
      "port": 8080
    },
    "health": { "path": "/health", "startupTimeoutSeconds": 60 },
    "storage": { "dataDirectory": "data", "mounts": [] }
  }
}
```

Automaton does not prescribe a language, framework, package manager, source
layout, build system, or application architecture.

## Fields

| Field | Meaning |
|---|---|
| `appId` | Remote app ID after linking. |
| `name` | App display name. |
| `server` | Optional configured Automaton server. |
| `application.build.command` | Optional executable, arguments, working directory, environment, and shell setting. |
| `application.build.image` | Optional OCI image used to execute the build command; defaults to `runtime.image`. |
| `application.build.outputDirectory` | Optional directory containing the build artifact. |
| `application.build.singlePageApp` | Static sites only, default `false`. A missing path answers 404. Set `true` for a single-page app whose router needs unknown paths to get `index.html` (200). Sites published by an older release lose the fallback when the Runner next rewrites its configuration: declare the field and redeploy them. |
| `application.build.timeoutSeconds` | Maximum build duration. |
| `application.runtime.command` | Executable and arguments for the long-running app process. |
| `application.runtime.image` | OCI image that executes the runtime command; required for new container deployments when `runtime.command` is present. Historical stored contracts remain readable until redeployed with an image. |
| `application.runtime.user` | Optional numeric UID or UID:GID for the runtime process. Keep the identity stable when the app must update files it created in persistent storage. |
| `application.runtime.update` | Optional, `side-by-side` (default) or `stop-first`. By default an update starts the new container while the old one keeps serving, and switches over when the new one is ready. An app that locks its data folder (a database or log store that holds a lock file) cannot run twice on it: set `stop-first`. The update then stops the old container, starts the new one, and starts the old one again if the new one fails. The app is down while it starts, so use it only when the app needs it. Servers run apps through Docker on a Machine Runner; `stop-first` has no effect on Compose apps. |
| `application.runtime.port` | Port requested by the app contract. |
| `application.health.path` | Optional HTTP readiness path. |
| `application.health.startupTimeoutSeconds` | Maximum startup duration. |
| `application.storage.dataDirectory` | Durable app data directory. |
| `application.kind` | Optional, `web` or `api`, for a running app. Missing means web. Static sites and actions-only apps are known from the rest of the contract. |
| `application.description` | Optional one line about the app, at most 200 characters. Each Action can add its own one-line `metadata.description` in `action.yaml` (same limit). |
| `application.homeCard` | Optional `{ "intervalSeconds": 900 }`: how often the platform runs the app's `home-card` Action (default 900, from 300 to 86400). See [Home card](#home-card-status-on-studio-home). |
| `application.pages` | Optional key paths of a web app, up to 10 entries such as `{ "path": "/", "label": "Home" }` (path starts with `/`, label up to 40 characters). |
| `application.openApi` | Optional path of an API app's OpenAPI document, such as `/openapi.json`. Studio lists the endpoints from it and shows the API reference button. |
| `application.openPaths` | Optional paths of the private address that answer without Automaton sign-in, for webhooks and callbacks. See below. |
| `application.publicAccess` | Optional hint `{ "level": "required" or "recommended", "reason": "..." }` for an app that outside callers reach (check-in URLs, a tracking script). New apps are private, so the install page shows it before installing and says to choose "Anyone on the internet" under "Who can open it". It changes nothing by itself. |

The build container's `/tmp` is small (a few tens of MB), so a build that unpacks a download or caches packages there can fail with "No space left on device". Work inside the app folder instead: unpack and cache under it, and point `HOME` and `TMPDIR` there (for example `export HOME="$PWD/.home" TMPDIR="$PWD/.tmp"; mkdir -p "$HOME" "$TMPDIR"` at the top of `build.sh`). When a build fails, `automaton deploy` and Studio show the last lines of its output.

The Runner supplies the effective port in `AUTOMATON_PORT` and the durable data
path in `AUTOMATON_APP_DATA_DIR`. Every runtime and Action also gets
`AUTOMATON_API_URL`, the server's public API base URL. A configured health path
is ready only after a 2xx HTTP response. On managed Linux Runners, the data directory and declared
storage mounts are prepared as writable directories for the app container. File
ownership and permissions still follow the UID that creates each file. Build
and runtime artifacts can be replaced.

Commands use structured arguments by default. Set `useShell` only when needed.
Keep managed paths inside the project workspace.

Run `automaton validate`, then `automaton deploy`. Deployment is asynchronous.
Wait for `Succeeded` before treating the app as deployed. Publishing is a
separate action and requires explicit approval.

## Home card: status on Studio Home

An app shows a short live card on Studio Home (pinned apps) through its Actions; there is nothing to push and no key. You send data only; Studio draws it in its own fonts and colours, in light and dark mode and at phone width. A card is a glance: detailed charts and statistics stay in the app itself.

- **An Action named `home-card`** returns the card as its whole result. The platform runs it for you every 15 minutes (set `application.homeCard.intervalSeconds`, at least 300; the first run of each app is spread across the interval) from the next deployment that includes it. It also runs once as soon as each deployment of the app is live, and people who can manage the app can choose **Refresh card** on its Home card to run it once more (at most once a minute per app). It runs in a lighter container than other Actions: 128 MB memory, 0.25 CPU, 64 processes and a 30-second limit (a server administrator can change these under `MachineContainerResources` as `HomeCardMemoryLimitMb`, `HomeCardCpuLimitCores`, `HomeCardPidsLimit` and `HomeCardTimeoutSeconds`). It works for static sites, web apps, APIs and Actions-only apps.
- **Any other Action** can include a `homeCard` object in its result, so an app that already runs Actions (a backup, a monitor) updates its card whenever it runs.

The card is plain text, shown as text and never as HTML. Tones are `ok`, `warning`, `critical` and `info`; a card never sends colours.

```json
{
  "status": "Healthy",
  "detail": "3 sites checked",
  "tone": "ok",
  "blocks": [
    { "type": "meter", "label": "Disk", "value": 61, "max": 100, "unit": "%", "warnAt": 75, "critAt": 90 },
    { "type": "counters", "items": [{ "label": "Up", "value": 12, "tone": "ok" }, { "label": "Down", "value": 1, "tone": "critical" }] },
    { "type": "chart", "kind": "line", "labels": ["Mon", "Tue", "Wed"], "series": [{ "name": "Response ms", "tone": "info", "values": [210, 190, 240] }] }
  ]
}
```

`status` is required (at most 80 characters); `detail` (240) and `tone` are optional and `tone` colours the card's top edge. `blocks` holds at most 6 blocks:

| Block | Fields |
|---|---|
| `fact` | `label` (40), `value` (120), `note`? |
| `badge` | `label`, `text` (40), `tone`, `note`? |
| `meter` | `label`, `value`, `max`, `unit`? (12), `warnAt`?, `critAt`?, `note`? |
| `counters` | `items`: 2 to 4 of `label`, `value` (number or short text), `tone`? |
| `list` | `rows`: up to 5 of `text`, `tone`?, `note`? |
| `alert` | `severity` (`info`, `warning`, `critical`), `text` (240); also goes to the notification feed |
| `buttons` | `items`: up to 2 of `label`, `path` (a page of the app itself, starting with `/`) |
| `chart` | `kind` (`line`, `area`, `bar`, `stacked-bar`, `donut`, `gauge`), `labels` (up to 48), `series`: `[{ name, tone, values }]` with one number per label; a `gauge` has one series with one value and an optional `max` (default 100) |

The older `facts` (up to 3 of `label`, `value`) and `alerts` (up to 5) lists keep working. An unknown block type is skipped. A card that breaks the rules (or is larger than 16 KB) shows the default card on Home with the reason, until the next valid card; a run that fails or times out keeps the previous card, which Home greys and shows when it was last updated. Home reads stored cards only; it never calls the app.

The schema is published at `/schemas/home-card.schema.json`. `automaton action run home-card` checks the result against it and prints each problem (for example `card.blocks[1].items needs 2 to 4 counters`).

Every Action of an app whose runtime is running also gets `AUTOMATON_APP_INTERNAL_URL`, the address of the running app on the Runner's private network (for example `http://10.0.0.5:32768`), so a `home-card` Action can ask its own app for numbers. Only that app's Actions receive it; it is not set when the app is not running.

## Start an Action from outside: webhooks

A webhook lets something outside Automaton (a GitHub release, a monitor, another server) start one of your app's Actions without a schedule or a person. In Studio open the app's **Actions** tab and choose **Add webhook** under the Action; from a terminal run `automaton action webhook add <action-key> --name "Docs walk"`. The secret is shown once, so copy it then; `automaton action webhook rotate <action-key or id>` replaces it and the old one stops working at once. `list` shows each webhook with its ID and last calls, and `delete` (or `remove`) deletes one. Add `--app <name or id>` to any of them to work on an app you may update without being in its linked folder.

Schedules use cron only: five fields (minute hour day-of-month month day-of-week, no seconds) and a time zone. `automaton action schedule list --app overseer` shows ID, Action, cron, time zone, on/off and next run (`--json` for scripts). `add <action> --cron "0 2 * * 1-5" --tz Europe/Bucharest` creates one and prints its next three runs; `edit <id> --cron "*/15 * * * *"` changes only the options you give; `on <id>` and `off <id>` switch it; `delete <id>` asks first (`--yes` skips the question). A bad cron expression or time zone is refused with the reason before anything is saved.

```bash
curl -X POST https://api.example.com/api/hooks/<webhook-id> \
  -H "Authorization: Bearer <secret>" -H "Content-Type: application/json" \
  -d '{"version": "0.2.175"}'
```

- The request's JSON body (at most 64 KB; empty means `{}`) becomes the Action's input and is checked against the Action's input schema. `X-Automaton-Webhook-Secret: <secret>` works instead of the `Authorization` header.
- A call without the right secret gets 404, the same as an address that does not exist. A disabled webhook answers 403; a paused app answers 409 and does not run.
- The Action runs as the person who created the webhook, and Activity shows the run as "started by webhook" with its name. A call while the Action is already running is skipped (409). Each webhook accepts 30 calls a minute (429 above that). Every call is audit-logged.
- Signature checks of services such as GitHub are not built in yet; send the secret from your own workflow.

## Open paths: webhooks and callbacks

An app's private address (`app-<id>.apps.<domain>`) asks every visitor to sign in to Automaton. An outside service that calls the app (a payment webhook, an OAuth callback) cannot sign in, so declare the paths it calls:

```json
"application": { "openPaths": ["/hooks/*", "/oauth/callback"] }
```

`/oauth/callback` opens exactly that path; `/hooks/*` opens `/hooks/` and everything below it. Name at least one folder or file (`/` and `/*` are refused) using only letters, digits and `/ - _ . ~`, at most 20 paths. Requests with `..` or encoded slashes never match an open path. Those paths answer anyone who knows the address, so the app must check the caller itself (a signature or secret). Everything else stays behind sign-in, and publishing the app still makes the whole public address open. The routes update with the next deployment.

## App secrets

Keep credentials out of `automaton.json` and source files. Set them as app secrets in Studio under **App → Settings → Secrets**: a name such as `S3_SECRET_KEY` (capital letters, digits and underscores) and its value. Values are stored encrypted, are never shown again after saving, and are passed as environment variables to each **Action** run and to the **running app** (API or server-rendered site) whenever it starts, including after a server restart. Action logs and results have them replaced by `[REDACTED]`. Saving an existing name replaces its value; a running app picks up the change on its next start, so press **Restart** on the app's Overview page (or in the Settings card) or redeploy it with `automaton deploy`. Read secrets from the environment in code (for example `process.env.STRIPE_KEY` or `os.environ["STRIPE_KEY"]`); never write them into source or build output. Static sites cannot use secrets, because their files are sent to the browser. Compose apps do not receive app secrets yet.

### Declared secrets and shared vaults

Declare the secrets an app needs in `application.secrets`:

```json
"secrets": [
  { "name": "STRIPE_KEY", "description": "Stripe secret key for payments" },
  { "name": "PAYMENTS_KEY", "from": "project:STRIPE_LIVE_KEY" }
]
```

An app receives every app secret, and for each declared name it does not have itself, a value from a shared vault:

1. The **project vault**, when the app belongs to exactly one project. Project administrators and developers set its secrets; every app of the project that declares the name receives it.
2. The **user vault** of the app's owner, only for personal apps (in no project, with one owner), so nobody else who can change an app's code can read a person's own secrets.

`"optional": true` marks a secret the app works without: the install page and `automaton catalog install` let it stay empty, and `automaton secrets status` does not report it as missing. `from` takes the value from a vault under another name (`project:NAME` or `user:NAME`). Vault secrets an app does not declare never reach it. A changed vault secret applies to every app using it at its next Action run or start.

From the app's folder:

| Command | Does |
|---|---|
| `automaton secrets status` | Lists declared secrets: present or missing, and which scope provides each. |
| `automaton secrets set NAME [--project\|--user] [--description TEXT]` | Saves a value read from standard input, or prompts without echo. Without a flag, it is an app secret. It works before the name is declared: an app secret reaches the app either way; declaring it makes `secrets status` and the install page list it. |
| `automaton secrets list [--project\|--user]` | Lists names, versions and descriptions, never values. |
| `automaton secrets delete NAME [--project\|--user]` | Deletes a secret. |
| `automaton project create NAME` | Creates a project; you become its administrator. |
| `automaton project list` | Lists the projects you belong to. |
| `automaton project add-app [PROJECT]` | Adds the current app to a project (name or id; optional when you are in one project). Needed before `secrets set --project`. |

`automaton validate` and `automaton deploy` warn about declared secrets without a value (`validate --strict` fails).

### Platform permissions

`application.permissions` requests platform access for the app, for example `"permissions": ["backups:read", "server:read"]`. `apps:read-status` (administrator-approved, read-only) lets a monitoring app read every app's live status, last deploy result, last and next Action run and, for apps installed from a catalog, its catalog category (`category`, null otherwise) at `GET /api/apps/status`. `logs:receive` makes the app the log database of this server: once an administrator approves it and the app runs, the API, the Worker and the Runner that hosts the app send their logs to it over OpenTelemetry (OTLP/HTTP, protobuf, `POST /insert/opentelemetry/v1/logs`) with no setting; the app must accept that on its own port, and the logs can contain addresses and user names, so install such an app as private. Pausing, uninstalling or revoking it stops the forwarding, and a `Logging__Otlp__Endpoint` set by the administrator always wins. `server:cleanup` lets it read, schedule and run the nightly host cleanup at `/api/server/cleanup` (administrator-approved). `notify:email` lets it email the app's owners, the server's administrators, its project's members and addresses on the administrators' allowed list through the server's SMTP, at `POST /api/notify/email` (10 emails per hour; see [Secrets and permissions](secrets-and-permissions.md)). `backups:read` lets the app list and download the encrypted server backups (`GET /api/install/backups`). `backups:run` lets it ask the server for a new backup (`POST /api/Install/backups/run`) for a viewer who is a system administrator, who must approve it (the app forwards the viewer token as for `server:cleanup`). `server:restart` lets it restart the server (`POST /api/Install/restart/run`, optional body `{"restartAtUtc": "2026-11-20T03:00:00Z"}`, at most 7 days ahead) for a viewer who is a system administrator, with the same viewer token; the call is refused with 409 while a backup, deploy, server update or Runner upgrade runs, and `GET /api/install/restart` (with `server:read`) returns `lastRestartedAtUtc` and any scheduled restart. `server:update-os` lets it install the server's OS (apt) updates (`POST /api/Install/os-updates/run`, optional body `{"scope": "security"}` (default) or `"all"`) for a viewer who is a system administrator, with the same viewer token; the call is refused with 409 while a backup, deploy, server update, Runner upgrade, restart or another OS update runs, and `GET /api/install/os-updates` (with `server:read`) returns the state, the number of updates installed, whether a restart is now needed, the progress while it runs (`totalCount`, `progressCount`, `currentPackage`), the packages held back and the end of the apt log. `server:read` lets it read the server's health, read-only, at `GET /api/server/health`: server and Runner versions and whether a newer release exists, OS name and version, uptime, CPU, load, memory and disk, Docker disk use, pending and security OS updates, restart required, certificate expiry and the last backup. For each app on a Runner (`runners[].apps`) it also lists disk use and, for apps with running containers, `cpuPercent` (100 is one core) and `memoryBytes` (null when none runs). The Machine Management Runner measures the host figures every 5 minutes; apps get no shell or file access. `GET /api/server/maintenance-runs` (also `server:read`) returns the last 20 maintenance runs, newest first: backups, OS updates, server restarts and Runner upgrades, each with `kind`, `requestedBy` (when known), `startedAtUtc`, `finishedAtUtc`, `state` (`running`, `succeeded`, `failed` or `refused`) and a `message`; the server keeps them even when no page is open. `security:read` lets it read fail2ban on the host, read-only, at `GET /api/server/security/fail2ban`: whether fail2ban runs, and per jail the current bans (address, when, until) and the bans in the last hour and 24 hours. The Runner reads fail2ban's database; the app gets no socket or firewall access. `security:manage` (administrator-approved, tier high) lets it unban or allowlist one address at `POST /api/server/security/fail2ban/unban` and `POST /api/server/security/fail2ban/allowlist` with the body `{"ip": "203.0.113.9"}` (one IPv4 or IPv6 address; the app forwards the viewer token of a system administrator as for `server:cleanup`). The Runner runs fixed fail2ban commands on the host; each request is audit-logged. The allowlist is fail2ban's live allowlist: it lasts until fail2ban reloads or restarts (a server update reloads it), so add a permanent one to `jail.local` as [Security](security.md) describes. `runner:host-exec` (tier high) lets an app run any command as root on one machine: `POST /api/server/runners/{runnerId}/host-exec` with `{"script": "...", "stdin": "...", "timeoutSeconds": 1800}` (default 30 minutes, at most 2 hours; the app forwards the viewer token of a system administrator). The script runs as root in the host's namespaces; `stdin` is where secrets go (never in the script, arguments or environment), and the answer holds `exitCode`, `timedOut`, `output` and `error` once the script ends, with every output line that repeats an input line replaced by `***`. It works only when a system administrator has turned on host access for the server (setting `HostExec.Enabled`, **Studio → an app's Admin tab**, off by default), only for apps owned by a system administrator, and only on machines an administrator approved for that app (`automaton permissions approve <app> runner:host-exec --runner <machine>`). An approval is tied to the app's version, commit, last deployment and owner: a new deploy or catalog update suspends it until the administrator approves again, a new owner revokes it, and turning the server switch off revokes every approval. Nothing of the script, its input or its output is stored; Activity records the app, machine, who started it, start and end time, exit code and a SHA-256 hash of the script. Risks: the approved app, and anyone who can change its code or the secrets it holds, controls that whole machine; approve it only for apps you trust and revoke it when you no longer need it. An app with both permissions uses one key for both. A system administrator of the server approves each request in Studio; the app then receives a scoped key as `APP_AUTOMATON_API_KEY` at its next Action run or start. `automaton permissions status` shows each request (waiting, approved, rejected or revoked) and the approval link to send to an administrator, and `automaton validate` and `automaton deploy` warn while a request waits. An app cannot approve its own requests, and neither can an ordinary API key. A system administrator can decide from a terminal with `automaton permissions list --pending`, `automaton permissions approve <app> <permission>` and `automaton permissions reject <app> <permission>` (add `--runner <machine>` for `runner:maintain` and `runner:host-exec`), using an admin-scoped key from Studio (**Settings → API Keys**, advanced key types, only an administrator sees it); with any other key the command is refused (403) and says to run `automaton login --permissions-admin`.

## Install manifest: settings and default schedules

Anyone can install a published app that allows forking from Studio. The installer is asked for what `automaton.json` declares, so an app needs no README steps:

```json
"settings": [
  { "name": "S3_REGION", "description": "Object storage region", "default": "us-east-1" },
  { "name": "S3_PREFIX", "description": "Folder for this install's copies", "required": true }
],
"schedules": [
  { "name": "Nightly upload", "action": "upload", "cron": "30 3 * * *", "timeZone": "Etc/UTC", "input": {} }
]
```

- **`settings`** are plain, non-secret values (`name` as for secrets, optional `description`, `default`, `required`). The installer fills them in; they reach Actions and the running app as environment variables, like secrets, and a `default` applies until a value is saved. A `default` containing `{{app.address}}` gets the app's own public address with no trailing slash (its primary verified own domain, otherwise its address on the apps domain; before the app is public, its private address), for example `"default": "{{app.address}}"`. The running app restarts when its address changes. Saving or clearing a setting restarts the running app so the container sees the new value. Unlike secrets, their values are not redacted from Action output, but they are write-only in Studio like secrets. Anything sensitive belongs in `secrets`, not here.
- **`schedules`** are offered to the installer, who accepts or skips each; accepted ones become ordinary Action schedules of the installed app. `action` is an Action key; give exactly one of `cron` (five fields) or `intervalSeconds`; `input` is an object.
- **`secrets`** and **`permissions`** work as above. Infrastructure permissions still need a system administrator's approval.

Every installed app also receives `AUTOMATON_APP_ID`, its own app ID. Use it (or the install's name) in a storage prefix so two installs of one app never share data.

`automaton validate` checks the shape of all of these (names, cron fields, one of cron or interval); a cron value or interval the server rejects only shows when the schedule is created.

**Installing.** On a published app's store page, **Install on your server** opens Studio's install page (`/apps/install/<app id>`; the app must allow forking). It asks for a name, the settings and secrets, shows the permissions (a system administrator can approve them right there; otherwise one must approve them on the app page) and lists the schedules. Install creates your own copy, saves the values, deploys it and creates the schedules you kept. Installing the same app again makes an independent copy with its own name, values, approvals and schedules.

**From the CLI.** `automaton catalog install` and `automaton catalog update` create the manifest's schedules the same way (they wait for the deployment first, because the schedules name Actions) and print which ones they created. `--no-schedules` skips them; `--schedule "Name"` (repeatable) keeps only those. `update` brings the schedules up to the new manifest: it adds ones the app does not have yet, updates a default schedule that is still as the previous version declared it (a new cron, time zone, input or name, so a renamed schedule is renamed in place), and removes one the new version dropped. A schedule you changed after the install (in Studio or with `automaton action schedule edit`) is never touched; the update lists it as kept. `automaton catalog update APP --check` lists these changes (and `--json` has them under `schedules`) without changing anything. A fresh `catalog install` creates every schedule the manifest declares, with its cron, time zone and input. An older CLI (before 0.2.174) does not create them: update the CLI.

An app owner can also change an installed app's schedules in Studio: on the **Actions** tab each Action lists its triggers (schedules and webhooks). **Edit** on a schedule opens a form to run it every N minutes or hours, every day at a time, or on a cron expression, in a time zone, with the next three run times shown; **Delete…** asks first, and **+ Add schedule** adds one under the Action. Cron is the only stored timing: "every" and "daily at" only write the cron expression, and a stored expression that fits one of them opens as that choice. People who may only view the app see none of these buttons.

## Create or link

Use `automaton new "<name>"` for a new remote app. Use `automaton link <app-id>`
for an existing remote app. Both commands refresh `AGENTS.md` and the local app
skill.
