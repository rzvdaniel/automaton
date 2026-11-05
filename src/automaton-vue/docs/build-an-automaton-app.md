---
title: Build an Automaton app
description: The app contract in practice; static sites, servers with a runtime, durable data and what to tell a coding agent.
---

# Build an Automaton app

An app is a folder with `automaton.json` and source in any language. This page shows the two common shapes. The complete field list is the [automaton.json reference](./automaton-json).

## Prerequisites

An app folder from [Create your first app](./getting-started).

## Static site

Build files, no server process. `automaton new "My Site" --template static` creates this shape: a build command that writes the files into `outputDirectory`, and a health file.

```json
{
  "name": "My Site",
  "application": {
    "build": {
      "command": { "executable": "sh", "arguments": ["-c", "mkdir -p dist && cp index.html health.txt dist/"] },
      "outputDirectory": "dist"
    },
    "health": { "path": "/health.txt" }
  }
}
```

Publishing allows only public file types (such as `.html`, `.css`, `.js`, `.txt`), so the health file is `health.txt`. Runners serve static output from one shared static server. Static sites cannot use [secrets](./secrets-and-permissions).

## Server with a runtime

For APIs, server-rendered sites and anything with logic. Declare `application.runtime.command`; the process must listen on `AUTOMATON_PORT` and answer `application.health.path`. Store durable files under `AUTOMATON_APP_DATA_DIR`. `--template node|python|dotnet` creates a working example.

Requests and responses are complete: WebSockets and streamed responses (Server-Sent Events) are not supported. For live views, poll a small JSON endpoint.

## Deploy and verify

```bash
automaton validate
automaton deploy
```

Wait for `Succeeded` and verify the real behavior at the URL. See [Deploy and publish an app](./deploy).

## For coding agents

`automaton new` writes `AGENTS.md` and `.automaton/agents/skills/automaton-app/SKILL.md`. An agent should read them and `automaton.json`, keep the contract accurate, run `automaton validate` then `automaton deploy`, wait for `Succeeded`, and verify the app before reporting success. Do not paste API keys into the conversation. Machine-readable entry point: <https://automaton.monster/llms.txt>.

## Common errors

- **Build tools exist locally but not on the Runner.** Set `application.build.image` to an image that has them.
- **Port mismatch.** Listen on `AUTOMATON_PORT`, not a fixed port.
- More: [Troubleshooting](./troubleshooting).
