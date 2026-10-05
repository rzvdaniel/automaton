---
title: Build a static app
description: Build and deploy a static app with an application-owned HTTP runtime.
---

# Build a static app

For an app that serves public files, choose any suitable technology and source
layout. An optional build creates the runtime artifact, but the artifact must
also contain or depend on an application-owned HTTP server. Automaton does not
host a build directory by itself. Declare both the build and runtime contract
in `automaton.json`.

```json
{
  "name": "My App",
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
    }
  }
}
```

The runtime process must listen on the effective `AUTOMATON_PORT`. A static or
frontend-only app still needs this HTTP runtime because its public URL is served
by the application-owned process. A runtime-less deployment is only suitable
for an Actions-only or other finite-work app with no HTTP endpoint.

Run `automaton validate`, then `automaton deploy`. Wait for `Succeeded` and
verify the deployed URL. Publish when it is within the requested app delivery or an existing standing authorization. Do not ask for separate publication approval again; honor any user restriction to keep the app private or unpublished. For authorized non-interactive publication, use `automaton publish --yes` and verify the published URL.

The [automaton.json reference](/automaton-json) documents the complete generic
contract.
