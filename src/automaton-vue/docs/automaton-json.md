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

Keep credentials out of `automaton.json` and source files. Set them as app secrets in Studio under **App → Settings → Secrets**: a name such as `S3_SECRET_KEY` (capital letters, digits and underscores) and its value. Values are stored encrypted, are never shown again after saving, and are passed to the app's **Actions** as environment variables; Action logs and results have them replaced by `[REDACTED]`. Saving an existing name replaces its value. Running apps (the HTTP runtime) do not receive app secrets yet.

## Create or link

Use `automaton new "<name>"` for a new remote app. Use `automaton link <app-id>`
for an existing remote app. Both commands refresh `AGENTS.md` and the local app
skill.
