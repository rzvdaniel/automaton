---
title: CLI recipes
description: Copy-paste commands for the common tasks - install, log in, deploy a folder, list apps, read logs, set a secret and update the server.
order: 7
---

# CLI recipes

One command for each common task. Run them in the app's folder unless a recipe says otherwise. Your AI runs the same commands; see [Work with AI](./work-with-ai). Every option: [CLI reference](./automaton-cli).

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

It prints an address and a code. Open it, approve, and the CLI notices within seconds. On a machine without a browser add `--device`. Check: `automaton whoami` names your server and you. More ways to sign in: [Log in without a browser](./automaton-cli#log-in-without-a-browser).

## Deploy a folder

New app:

```bash
automaton new "My App" --template node   # or python, dotnet, static
automaton validate
automaton deploy
```

An existing folder that has an `automaton.json` is created with `automaton create` instead of `new`. With several Runners add `--runner NAME`.

`Deploy complete` means the package was applied, not that the app runs. Wait until `automaton apps` shows `Running` for it. Then open it: `automaton open` (or **Apps** in Studio).

A static site answers only after `automaton publish --yes --slug my-app`. More on sharing and publishing: [Deploy and publish an app](./deploy).

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

The value comes from standard input, never from an argument, so it stays out of your shell history. `secrets list` shows names only. A running app reads the new value after its next start; `automaton deploy` applies it now. `automaton secrets status` shows which secrets `automaton.json` declares and which are missing. See [Secrets and permissions](./secrets-and-permissions).

## Update the server

You must be a system administrator of the server you are logged in to.

```bash
automaton server update --wait
```

It installs the latest release, then checks that the server is healthy and prints the installed version. Add `--runners` to also upgrade Runners on an old image. `automaton server update-status` shows the last run. Details and recovery: [Update the server](./update-server).

## Something else

`automaton --help` lists every command, and `automaton <command> --help` its options.
