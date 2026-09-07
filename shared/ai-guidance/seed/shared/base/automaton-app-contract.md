# Automaton app contract

## Application contract

The `application` object in `automaton.json` describes how Automaton builds,
runs, checks, and stores the app.

Automaton does not prescribe a language, framework, package manager, source
layout, build system, or application architecture.

## Generic contract

```json
{
  "application": {
    "build": {
      "image": "runtime-image:tag",
      "command": {
        "executable": "build-tool",
        "arguments": ["build"],
        "workingDirectory": ".",
        "environment": {},
        "useShell": false
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
        "environment": {},
        "useShell": false
      },
      "port": 8080
    },
    "health": { "path": "/health", "startupTimeoutSeconds": 60 },
    "storage": { "dataDirectory": "data", "mounts": [] }
  }
}
```

`build.command` is optional. When present, Automaton runs its executable with
its arguments, working directory, environment, shell setting, and timeout.
When `build.image` is omitted, `runtime.image` is used.
`build.outputDirectory` is optional and identifies the build artifact to use.

`runtime.command` names the process to start. It is required when the application
exposes an HTTP or public endpoint, including frontend-only and static web apps.
For a new container deployment it requires an explicit `runtime.image`; the image
owns the application process. Historical stored contracts without an image remain
readable, but must declare one before they can be deployed again.
Those apps must provide or depend on an HTTP server in the runtime artifact and
declare it through this contract. Automaton has no static-file serving fallback.
A null `runtime.command` is valid only for Actions-only or other finite-work
applications with no HTTP endpoint.

Automaton supplies the effective port in `AUTOMATON_PORT`, regardless of the
requested `runtime.port`. The process must listen on that port. Runtime command
fields use the same execution rules as the build command.

`health.path` is optional. When configured, readiness requires a successful
2xx HTTP response on that path. `health.startupTimeoutSeconds` limits startup.

`storage.dataDirectory` is the durable application data directory. Automaton
supplies its path in `AUTOMATON_APP_DATA_DIR`. Data there survives runtime
replacement. Build and runtime artifacts are disposable.

Use structured commands by default. Set `useShell` only when shell semantics
are required. Keep every managed path inside the application workspace.

Run `automaton validate`, then `automaton deploy`. Deployment is asynchronous.
Success means the deployment operation reaches `Succeeded`. Publishing is a
separate action and requires explicit user approval.
