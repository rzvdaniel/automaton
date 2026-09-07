# Automaton app workflow

Use this workflow for any app in this folder.

The app contract is the source of truth for build and runtime behavior. Inspect
the existing files before choosing commands, paths, or dependencies.

1. Inspect `automaton.json` and the current app link.
2. If `automaton.json` is missing, run `automaton new "<name>"` for a new
   remote app or `automaton link <app-id>` for an existing remote app.
3. If `automaton.json` exists but is not linked in the current environment, run
   `automaton create` for a new remote app for this scaffold or
   `automaton link <app-id>` for an existing remote app.
4. If the project is linked, continue with the normal workflow.
5. Understand the requested application.
6. Choose any suitable technology and source layout.
7. Implement the app.
8. Fill the `application` fields in `automaton.json` for build, runtime,
   health, and storage.
9. Run `automaton validate` and fix its errors.
10. Run `automaton deploy`.
11. Wait for the asynchronous operation to reach a terminal result.
12. If it reaches `Succeeded`, verify the deployed URL.
13. If it fails, inspect the exact failure and fix it.
14. Never publish unless the user explicitly requests it.

Prefer a Standard or Hosted Runner for ordinary applications. Do not use a
Privileged Host Runner as an automatic fallback. Privileged Host approval is
persisted per app environment: after explicit user approval, run
`automaton target approve <environment>` and use
`automaton target revoke <environment>` to remove it. If only a Privileged
Host Runner appears usable, stop and ask the user for explicit approval.

Any app that exposes an HTTP/public URL must declare `application.runtime.command`.
This includes frontend-only and static web apps. Their runtime artifact must
contain or depend on an HTTP server described by the same generic contract.
A null runtime command is reserved for Actions-only or other finite-work apps
with no HTTP endpoint. Make the process, port, health path, and data directory
explicit whenever the app has a runtime. Automaton has no static-file serving
fallback.
Keep generated output and dependencies out of durable source unless the user
specifically asks for them.

Automaton does not require a language, framework, package manager, source
layout, or application architecture. The remote Runner performs the configured
build. The app must listen on the effective port supplied through
`AUTOMATON_PORT`. Put durable data under `AUTOMATON_APP_DATA_DIR`; build and
runtime outputs are disposable. Prefer structured commands and use shell mode
only when required.

Use the smallest contract that describes the app. Do not invent a directory
convention, framework, or service shape to satisfy this guidance. Preserve
existing app metadata and unrelated user files while editing.

```json
{
  "application": {
    "build": {
      "image": "build-image:tag",
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

The runtime image is explicit and is not inferred from the executable or source
files. For illustration only, a project may use `node:22-bookworm-slim` with
`node server.js`, `python:3.13-slim` with `python app.py`, or an ASP.NET runtime
image with its published application command. Choose the image and command for
the application; Automaton does not detect or select a stack.
