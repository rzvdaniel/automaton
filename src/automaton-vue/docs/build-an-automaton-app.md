---
title: Build an Automaton app
description: The app contract in practice; static sites, servers with a runtime, durable data and what to tell a coding agent.
---

# Build an Automaton app

An app is a folder with `automaton.json` and source in any language. This page shows the two common shapes. The complete field list is the [automaton.json reference](./automaton-json).

## Prerequisites

A server with a Runner and a signed-in CLI ([Install with your AI agent](./install-with-ai)), or your administrator's API URL (see [Install and authenticate](./automaton-cli#install-and-authenticate)).

## Create the app

1. Check the CLI and the server.

   ```bash
   automaton whoami
   automaton runners
   ```

   Check: your server is shown and a Runner is connected. `automaton: command not found` means `~/.dotnet/tools` is not on `PATH`. With several servers the new login stays a separate profile; see [several servers](./automaton-cli#several-servers).

2. Create the app in an empty folder. `--template` is `node`, `python`, `dotnet` or `static`; omit it for an empty contract.

   ```bash
   mkdir my-app && cd my-app
   automaton new "My App" --template node
   ```

   This writes `automaton.json`, `AGENTS.md` and `.automaton/agents/skills/automaton-app/SKILL.md`, and creates the app on the server. For an existing app use `automaton link <app-id>`; for missing guidance files use `automaton sync-ai`.

3. Edit. Read `AGENTS.md` and `automaton.json` first. Keep data that must survive redeploys in the app's data directory (the template's `AGENTS.md` and `automaton.json` name the exact variable, `AUTOMATON_APP_DATA_DIR`). Other files are replaced on every deploy.

4. Validate and deploy as described in [Deploy and publish an app](./deploy). `deploy` has no `--yes` flag, can print `Deploy complete` before the app is running, and you must wait for `Running` in `automaton apps` before you publish or open the address.

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

Server-Sent Events and WebSockets work: responses stream to the browser as the app writes them, and WebSocket upgrades are relayed. Each app can hold a limited number of open streams and sockets at once (20 by default), and an idle one is closed after five minutes, so send a heartbeat.

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
- **`Template would overwrite files`.** Use an empty folder or omit `--template`.
- **Login does nothing.** The server URL must be the API URL, not Studio.
- **Deploy works, app not Running.** Read `automaton logs`; the app must listen on `AUTOMATON_PORT` and answer its health path.
- **Port mismatch.** Listen on `AUTOMATON_PORT`, not a fixed port.
- More: [Troubleshooting](./troubleshooting).
