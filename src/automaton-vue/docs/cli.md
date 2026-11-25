---
title: CLI recipes
description: Copy-paste commands for the common tasks - install, log in, deploy a folder, list apps, read logs, set a secret and update the server.
order: 7
---

# CLI recipes

One command for each common task. Run them in the app's folder unless a recipe says otherwise. Your AI runs the same commands; see [Work with AI](./work-with-ai). Every command: `automaton --help`.

## Install the CLI

Linux or macOS:

```bash
curl -fsSL https://github.com/rzvdaniel/automaton/releases/latest/download/install.sh | sh
```

Windows (PowerShell): `irm https://github.com/rzvdaniel/automaton/releases/latest/download/install.ps1 | iex`

Check: `automaton --version` prints a version. `command not found` means `~/.local/bin` (Windows: `%LOCALAPPDATA%\Automaton\bin`) is not on your `PATH`.

## Log in

```bash
automaton login --server https://api.yourdomain.com
```

It prints an address and a code. Open it, approve, and the CLI notices within seconds. On a machine without a browser add `--device`. Check: `automaton whoami` names your server and you. Other ways to sign in are under [Log in without a browser](#log-in-without-a-browser).

## Deploy a folder

New app:

```bash
automaton new "My App" --template node   # or python, dotnet, static
automaton validate
automaton deploy
```

An existing folder that has an `automaton.json` is created with `automaton create` instead of `new`. With several Runners add `--runner NAME`.

`Deploy complete` means the package was applied, not that the app runs. Wait until `automaton apps` shows `Running` for it. Then open it: `automaton open` (or **Apps** in Studio).

A static site answers only after `automaton publish --yes --slug my-app`. Details are in [Publish, pause and delete](#publish-pause-and-delete).

## List apps

```bash
automaton apps
```

Shows ID, name, Runner, status and public address. `automaton status` shows the app linked to this folder.

## Read logs

```bash
automaton logs
```

It opens the app's runtime logs in Studio and prints the address, so on a server without a browser copy the address into a browser on your computer. In Studio the same view is **Apps**, your app, **Logs**.

## Set a secret

```bash
printf '%s' "the-value" | automaton secrets set MY_KEY
automaton secrets list
```

The value comes from standard input, never from an argument, so it stays out of your shell history. `secrets list` shows names only. A running app reads the new value after its next start; `automaton deploy` applies it now. `automaton secrets status` shows which secrets `automaton.json` declares and which are missing. See [Build an app](./build-an-app#secrets).

## Update the server

You must be a system administrator of the server you are logged in to.

```bash
automaton server update --wait
```

It installs the latest release, then checks that the server is healthy and prints the installed version. Add `--runners` to also upgrade Runners on an old image. `automaton server update-status` shows the last run. Details: [Update the server and Runners](./install#update-the-server-and-runners).

## Log in without a browser

Over SSH or for an AI agent: `automaton login --device --server https://api.example.com` prints an approval address and a code, then waits. Open the address in a browser on another device, sign in and approve; the CLI notices within seconds. The output contains the code, so do not share it. Codes expire after a few minutes.

For unattended jobs, create a key in Studio (**API Keys**; shown once) and run `automaton login --server https://api.example.com --key`, which asks for it at a hidden prompt. Or set `AUTOMATON_SERVER` and `AUTOMATON_API_KEY` in the process environment. Never put a key in an argument, a log or an AI conversation.

## Publish, pause and delete

```bash
automaton publish --yes --slug my-app   # public at https://my-app.apps.<domain>
automaton share                         # who can open it (same choices as Studio's Share dialog)
automaton pause                         # stop it, keep data, settings and address; resume with: automaton resume
automaton delete --yes                  # permanent: app, runtime, data and address
```

New apps are private: their `app-<id>.apps...` address needs sign-in, so an anonymous `curl` gets 401. Publishing waits up to a minute for the certificate. `automaton deploy --publish` redeploys and republishes an already published app. Without a linked folder use `automaton apps pause|resume|delete <name-or-id>`. A paused app answers 503 with a short "This app is paused" page.

## List in the app store and install from a catalog

A published app is listed in your server's store. Set how it appears:

```bash
automaton listing set --summary "A bluffing game" --category Games --tags "game,multiplayer"
automaton listing cover cover.png
automaton listing add-screenshot lobby.png
automaton listing show
```

Summary up to 200 characters, description up to 500; images are PNG, JPG, WEBP or GIF up to 2 MB (one cover, up to 8 screenshots). `summary` and `description` at the top of `automaton.json` fill the listing while it has none. `automaton publish --install` also lets others install a copy of your app.

To install from a catalog (**Apps → Catalog** in Studio, read from the official apps repository; sources are added by a system administrator under **Catalog sources**):

```bash
automaton catalog list
automaton catalog install <id> --name NAME --runner RUNNER --setting NAME=VALUE --secrets-file FILE --wait
```

Secrets come only from a file, `--secrets-stdin` or a hidden prompt, never from an argument. An app with missing secrets stops with "Missing secret(s): NAME"; put each `NAME=value` in the file and rerun. Permissions are never granted by installing: a system administrator approves them. Only add catalog sources you trust: an app is code that runs on your Runners.

## All commands

Run `automaton <command> --help` for a command's options.

<!-- cli-commands:start (generated by scripts/generate-cli-reference.mjs; do not edit) -->

| Command | What it does |
| --- | --- |
| `automaton new <name>` | Create a new Automaton app and link this directory |
| `automaton sync-ai` | Refresh the generated local AI guidance files in this folder |
| `automaton login` | Authorize the Automaton CLI or validate an existing API key |
| `automaton profiles` | List and select server profiles |
| `automaton logout` | Clear stored CLI credentials |
| `automaton whoami` | Show the current authenticated Automaton user |
| `automaton create` | Create a remote Automaton app for the current folder and link the selected local profile |
| `automaton delete` | Delete the remote app linked to this folder |
| `automaton status` | Show the current linked app, deploy settings, and publish state |
| `automaton current-runner` | Show the Runner assigned to the Live environment |
| `automaton validate` | Validate the current project before deploy or publish |
| `automaton action` | Validate and run Actions in the linked App; `action schedule list\|add\|edit\|on\|off\|delete` manages an Action's cron schedules and `action webhook add\|list\|rotate\|delete` the webhooks that start it from outside; add `--app <name\|id>` to work on any app you may update without a linked folder |
| `automaton secrets` | Check and fill the secrets this app declares in automaton.json (application.secrets) |
| `automaton project` | Create projects, list yours and add the current app to one |
| `automaton permissions` | Show the platform permissions this app requests and whether an administrator approved them |
| `automaton init` | Create a new Automaton app or link an existing one to this directory |
| `automaton link <app-id-or-slug>` | Link this directory to an existing Automaton app |
| `automaton deploy <target>` | Build, upload, and deploy the app to Automaton |
| `automaton open <environment>` | Open the current app in the browser |
| `automaton docs <app>` | Print an app's docs (docs/README.md), or its description when it ships none |
| `automaton logs <environment>` | Open runtime logs for an environment |
| `automaton pull <app>` | Download editable app source files from the server |
| `automaton publish` | Make the latest deployed app publicly accessible |
| `automaton share` | Show or change who can open this app (same choices as Studio's Share dialog) |
| `automaton pause` | Pause the linked app: stop it, switch its schedules off and show a paused page at its address. Data and settings are kept. |
| `automaton resume` | Resume the linked app and turn its schedules back on |
| `automaton runners` | List runners available on your Automaton account |
| `automaton apps` | List apps in your personal workspace |
| `automaton installation-status` | Show server version, resource counts and last backup |
| `automaton notifications [--json]` | List your notifications: server update, expiring certificate, offline Runner, failed backup, permission requests, failing apps. `acknowledge <key>` hides one |
| `automaton server` | Update the Automaton server you are logged in to |
| `automaton set-runner <runner>` | Assign or clear the Runner for the Live environment |
| `automaton config` | View or update global CLI settings |
| `automaton target` | Manage deployment targets for the linked app |
| `automaton listing` | Set how the linked app appears in the public marketplace: summary, description, category, tags, cover image and screenshots. Publishing lists an app by default. |
| `automaton catalog` | Create and build an apps repository for use as a catalog source, and manage the server's catalog sources |

<!-- cli-commands:end -->

## Something else

`automaton --help` lists every command, and `automaton <command> --help` its options.
