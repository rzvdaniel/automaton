# Automaton App Contract (Compact)

Use this contract for in-product app editing and AI-assisted app creation.

## Source and contract

- Keep `automaton.json` at the app root when it exists.
- The `application` object in `automaton.json` is the source of truth for build,
  runtime, readiness, environment, and persistent storage behavior.
- Source layout is the application's choice. Do not require a frontend folder,
  backend folder, package manifest, language, framework, or package manager.
- Studio edits application source; the CLI validates and deploys the same
  contract.

## Generic application contract

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

Commands are structured by default. Use shell mode only when the application
intentionally needs shell semantics. Paths are workspace-relative and validated
by Automaton. Environment values must not contain secrets in source-controlled
files.

The canonical fields are `application.build.command`,
`application.runtime.command`, `application.health`, and
`application.storage`.

`runtime.command` is optional for applications that only produce a static
artifact or perform finite work. When present, the Runner starts that process,
checks TCP or the configured HTTP health path, and exposes the healthy endpoint.
Persistent application data belongs under the configured data directory and is
not disposable runtime output.

## Deployment and publishing

- Run `automaton deploy` after validating the project and contract.
- The remote Runner performs the configured build once when a build command is
  present; the CLI does not implicitly build the same command locally first.
- Ask before publishing an application publicly.

Action modules remain a separate finite-work feature with their own manifest and
execution semantics. The deferred legacy controller/FastAPI bridge and its
Python-specific guidance are compatibility material, not requirements of this
generic contract.
