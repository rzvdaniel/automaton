---
title: automaton.json reference
description: Reference for the Automaton app identity and generic build, runtime, health, and storage contract.
---

# automaton.json reference

`automaton.json` lives at the project root. It links the project to an Automaton
app and declares the application contract.

## Generic application shape

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
| `application.runtime.port` | Port requested by the app contract. |
| `application.health.path` | Optional HTTP readiness path. |
| `application.health.startupTimeoutSeconds` | Maximum startup duration. |
| `application.storage.dataDirectory` | Durable app data directory. |
| `application.kind` | Optional, `web` or `api`, for a running app. Missing means web. Static sites and actions-only apps are known from the rest of the contract. |
| `application.description` | Optional one line about the app, at most 200 characters. Each Action can add its own one-line `metadata.description` in `action.yaml` (same limit). |
| `application.pages` | Optional key paths of a web app, up to 10 entries such as `{ "path": "/", "label": "Home" }` (path starts with `/`, label up to 40 characters). |
| `application.openApi` | Optional path of an API app's OpenAPI document, such as `/openapi.json`. Studio lists the endpoints from it and shows the API reference button. |
| `application.openPaths` | Optional paths of the private address that answer without Automaton sign-in, for webhooks and callbacks. See below. |
| `application.publicAccess` | Optional hint `{ "level": "required" or "recommended", "reason": "..." }` for an app that outside callers reach (check-in URLs, a tracking script). New apps are private, so the install page shows it before installing and says to choose "Anyone on the internet" under "Who can open it". It changes nothing by itself. |

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
| `automaton secrets set NAME [--project\|--user] [--description TEXT]` | Saves a value read from standard input, or prompts without echo. Without a flag, it is an app secret. |
| `automaton secrets list [--project\|--user]` | Lists names, versions and descriptions, never values. |
| `automaton secrets delete NAME [--project\|--user]` | Deletes a secret. |
| `automaton project create NAME` | Creates a project; you become its administrator. |
| `automaton project list` | Lists the projects you belong to. |
| `automaton project add-app [PROJECT]` | Adds the current app to a project (name or id; optional when you are in one project). Needed before `secrets set --project`. |

`automaton validate` and `automaton deploy` warn about declared secrets without a value (`validate --strict` fails).

### Platform permissions

`application.permissions` requests platform access for the app, for example `"permissions": ["backups:read", "server:read"]`. `notify:email` lets it email the app's owners, the server's administrators, its project's members and addresses on the administrators' allowed list through the server's SMTP, at `POST /api/notify/email` (10 emails per hour; see [Secrets and permissions](secrets-and-permissions.md)). `backups:read` lets the app list and download the encrypted server backups (`GET /api/install/backups`). `server:read` lets it read the server's health, read-only, at `GET /api/server/health`: server and Runner versions and whether a newer release exists, OS name and version, uptime, CPU, load, memory and disk, Docker disk use, pending and security OS updates, restart required, certificate expiry and the last backup. The Machine Management Runner measures the host figures every 5 minutes; apps get no shell or file access. An app with both permissions uses one key for both. A system administrator of the server approves each request in Studio; the app then receives a scoped key as `APP_AUTOMATON_API_KEY` at its next Action run or start. `automaton permissions status` shows each request (waiting, approved, rejected or revoked) and the approval link to send to an administrator, and `automaton validate` and `automaton deploy` warn while a request waits. An app cannot approve its own requests, and neither can an API key.

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

- **`settings`** are plain, non-secret values (`name` as for secrets, optional `description`, `default`, `required`). The installer fills them in; they reach Actions and the running app as environment variables, like secrets, and a `default` applies until a value is saved. Unlike secrets, their values are not redacted from Action output, but they are write-only in Studio like secrets. Anything sensitive belongs in `secrets`, not here.
- **`schedules`** are offered to the installer, who accepts or skips each; accepted ones become ordinary Action schedules of the installed app. `action` is an Action key; give exactly one of `cron` (five fields) or `intervalSeconds`; `input` is an object.
- **`secrets`** and **`permissions`** work as above. Infrastructure permissions still need a system administrator's approval.

Every installed app also receives `AUTOMATON_APP_ID`, its own app ID. Use it (or the install's name) in a storage prefix so two installs of one app never share data.

`automaton validate` checks the shape of all of these (names, cron fields, one of cron or interval); a cron value or interval the server rejects only shows when the schedule is created.

**Installing.** On a published app's store page, **Install on your server** opens Studio's install page (`/apps/install/<app id>`; the app must allow forking). It asks for a name, the settings and secrets, shows the permissions (a system administrator can approve them right there; otherwise one must approve them on the app page) and lists the schedules. Install creates your own copy, saves the values, deploys it and creates the schedules you kept. Installing the same app again makes an independent copy with its own name, values, approvals and schedules.

## Create or link

Use `automaton new "<name>"` for a new remote app. Use `automaton link <app-id>`
for an existing remote app. Both commands refresh `AGENTS.md` and the local app
skill.
