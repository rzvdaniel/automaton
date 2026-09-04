# Automaton app workflow

Use this workflow for any app in this folder.

The app contract is the source of truth for build and runtime behavior. Inspect
the existing files before choosing commands, paths, or dependencies.

1. Inspect `automaton.json`.
2. If it is missing and the user wants a new remote app, run
   `automaton new "<name>"`.
3. If the user names an existing remote app, run `automaton link <app-id>`.
4. Understand the requested application.
5. Choose any suitable technology and source layout.
6. Implement the app.
7. Fill the `application` fields in `automaton.json` for build, runtime,
   health, and storage.
8. Run `automaton validate` and fix its errors.
9. Run `automaton deploy`.
10. Wait for the asynchronous operation to reach a terminal result.
11. If it reaches `Succeeded`, verify the deployed URL.
12. If it fails, inspect the exact failure and fix it.
13. Never publish unless the user explicitly requests it.

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
