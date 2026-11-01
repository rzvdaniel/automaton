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
| `application.build.timeoutSeconds` | Maximum build duration. |
| `application.runtime.command` | Executable and arguments for the long-running app process. |
| `application.runtime.image` | OCI image that executes the runtime command; required for new container deployments when `runtime.command` is present. Historical stored contracts remain readable until redeployed with an image. |
| `application.runtime.user` | Optional numeric UID or UID:GID for the runtime process. Keep the identity stable when the app must update files it created in persistent storage. |
| `application.runtime.port` | Port requested by the app contract. |
| `application.health.path` | Optional HTTP readiness path. |
| `application.health.startupTimeoutSeconds` | Maximum startup duration. |
| `application.storage.dataDirectory` | Durable app data directory. |

The Runner supplies the effective port in `AUTOMATON_PORT` and the durable data
path in `AUTOMATON_APP_DATA_DIR`. A configured health path is ready only after a
2xx HTTP response. On managed Linux Runners, the data directory and declared
storage mounts are prepared as writable directories for the app container. File
ownership and permissions still follow the UID that creates each file. Build
and runtime artifacts can be replaced.

Commands use structured arguments by default. Set `useShell` only when needed.
Keep managed paths inside the project workspace.

Run `automaton validate`, then `automaton deploy`. Deployment is asynchronous.
Wait for `Succeeded` before treating the app as deployed. Publishing is a
separate action and requires explicit approval.

## App secrets

Keep credentials out of `automaton.json` and source files. Set them as app secrets in Studio under **App → Settings → Secrets**: a name such as `S3_SECRET_KEY` (capital letters, digits and underscores) and its value. Values are stored encrypted, are never shown again after saving, and are passed as environment variables to each **Action** run and to the **running app** (API or server-rendered site) whenever it starts, including after a server restart. Action logs and results have them replaced by `[REDACTED]`. Saving an existing name replaces its value; a running app picks up the change on its next start, so redeploy it with `automaton deploy`. Read secrets from the environment in code (for example `process.env.STRIPE_KEY` or `os.environ["STRIPE_KEY"]`); never write them into source or build output. Static sites cannot use secrets, because their files are sent to the browser. Compose apps do not receive app secrets yet.

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

`from` takes the value from a vault under another name (`project:NAME` or `user:NAME`). Vault secrets an app does not declare never reach it. A changed vault secret applies to every app using it at its next Action run or start.

From the app's folder:

| Command | Does |
|---|---|
| `automaton secrets status` | Lists declared secrets: present or missing, and which scope provides each. |
| `automaton secrets set NAME [--project\|--user] [--description TEXT]` | Saves a value read from standard input, or prompts without echo. Without a flag, it is an app secret. |
| `automaton secrets list [--project\|--user]` | Lists names, versions and descriptions, never values. |
| `automaton secrets delete NAME [--project\|--user]` | Deletes a secret. |

`automaton validate` and `automaton deploy` warn about declared secrets without a value (`validate --strict` fails).

## Create or link

Use `automaton new "<name>"` for a new remote app. Use `automaton link <app-id>`
for an existing remote app. Both commands refresh `AGENTS.md` and the local app
skill.
