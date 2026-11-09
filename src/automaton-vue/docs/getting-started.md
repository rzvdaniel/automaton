---
title: Your first app
description: Create an app from a template, edit it, validate, deploy, wait for Running, publish it and open its address.
---

# Your first app

This page takes you from a signed-in CLI to a published app. You need a server with a Runner and a signed-in CLI: [Install with your AI agent](./install-with-ai) does all three, or use your administrator's API URL and see [Install and authenticate](./automaton-cli#install-and-authenticate). For all deploy options see [Deploy and publish an app](./deploy).

## Steps

1. Check the CLI and the server.

   ```bash
   automaton whoami
   automaton runners
   ```

   Check: your server is shown and a Runner is connected. `automaton: command not found` means `~/.dotnet/tools` is not on `PATH`. Several servers: the new login stays a separate profile; see [several servers](./automaton-cli#several-servers).

2. Create the app in an empty folder. `--template` is `node`, `python`, `dotnet` or `static`; omit it for an empty contract.

   ```bash
   mkdir my-app && cd my-app
   automaton new "My App" --template node
   ```

   This writes `automaton.json`, `AGENTS.md` and `.automaton/agents/skills/automaton-app/SKILL.md`, and creates the app on the server. An existing app: `automaton link <app-id>`; missing guidance files: `automaton sync-ai`.

3. Edit. Read `AGENTS.md` and `automaton.json` first. Keep data that must survive redeploys in the app's data directory (the template's `AGENTS.md` and `automaton.json` name the exact variable, `AUTOMATON_APP_DATA_DIR`). Other files are replaced on every deploy. See [Build an Automaton app](./build-an-automaton-app) and the [automaton.json reference](./automaton-json).

4. Validate and deploy.

   ```bash
   automaton validate
   automaton deploy
   ```

   `deploy` has no `--yes` flag; `--yes` after `deploy` is read as an environment name. It can print `Deploy complete` before the app is running.

5. Wait for `Running`. Repeat until the Status column says `Running`; read `automaton logs` if it does not.

   ```bash
   automaton apps
   ```

6. Publish, if the user asked for it, then open the address. `--yes` skips the prompt.

   ```bash
   automaton publish --yes --slug my-app
   ```

   Open `https://my-app.apps.<domain>`. Check the real behavior, not only the status. A private (unpublished) app's address answers `401` to an anonymous request; open it in a signed-in browser.

## Common errors

- **`Template would overwrite files`.** Use an empty folder or omit `--template`.
- **Login does nothing.** The server URL must be the API URL, not Studio.
- **Deploy works, app not Running.** `automaton logs`; the app must listen on `AUTOMATON_PORT` and answer its health path.
- More: [Troubleshooting](./troubleshooting), [Deploy and publish an app](./deploy).
