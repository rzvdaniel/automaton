---
title: Create your first app
description: Install the CLI, sign in to your server, create an app from a template and hand it to a coding agent or edit it yourself.
---

# Create your first app

This page installs the CLI, signs in and creates an app folder with `automaton.json` and agent guidance. Then [deploy it](./deploy).

## Prerequisites

- A server and a Runner ([install](./install-server), [Runner](./deploy-runner)), or an API URL from your administrator.
- The .NET 10 SDK or later on your computer (for the NuGet tool only). Ubuntu 22.04 and Debian 12 have no .NET 10 package in apt: install it per user with the commands in [Install and authenticate](./automaton-cli#install-and-authenticate).

## Steps

1. Install the CLI.

   ```bash
   dotnet tool install -g Automaton.Cli
   automaton --help
   ```

2. Sign in to your server and approve the request in Studio. Do not paste keys into AI chat. For unattended use set `AUTOMATON_API_KEY` in the process environment.

   ```bash
   automaton login --server https://api.example.com
   ```

   On a server without a browser (SSH, an AI agent) use `automaton login --device --server https://api.example.com`. It prints an approval URL and a code, then waits; approve it from a signed-in session or with the terminal snippet in [Log in without a browser](./automaton-cli#log-in-without-a-browser). The `--device` output contains the code: do not share it.

   **Already signed in to another server?** The new login is saved as a separate profile named after the server, but your current profile stays the default, so later commands still go to the old server. Use `--profile NAME` (for example `automaton login --profile mine --server https://api.example.com`) and add `--profile NAME` to every later command, or run `automaton profiles use NAME` once, or set `AUTOMATON_PROFILE`. Check which one is active with `automaton profiles`. See [CLI reference](./automaton-cli#several-servers).

3. Create the app in an empty folder. `--template` writes a runnable starter: `node`, `python`, `dotnet` or `static`. Omit it for an empty contract.

   ```bash
   mkdir my-app && cd my-app
   automaton new "My App" --template node
   ```

   This writes `automaton.json`, `AGENTS.md` and `.automaton/agents/skills/automaton-app/SKILL.md`, and creates the app on your server.

4. Build the app. With a coding agent, give it a plain request such as `Build [APP IDEA] as an Automaton app and deploy it`. It reads `AGENTS.md`, which points to the same guidance as <https://automaton.monster/llms.txt>. Without an agent, edit the source and [`automaton.json`](./automaton-json) yourself.

5. Check the contract.

   ```bash
   automaton validate
   ```

## Verify it worked

`automaton whoami` shows your server, `automaton apps` lists the new app, and `automaton validate` reports no errors.

For an app that already exists on the server use `automaton link <app-id>`. `automaton sync-ai` restores the local guidance files if they are missing.

## Common errors

- **`automaton: command not found`.** Add `~/.dotnet/tools` to `PATH`.
- **Login does nothing.** Approve the login in your own Studio; the URL must be the API URL, not the Studio or project website URL.
- **Template would overwrite files.** Use an empty folder or omit `--template`.

Next: [Deploy and publish an app](./deploy).
