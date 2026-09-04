---
title: Automaton CLI reference
description: CLI commands for creating, linking, validating, deploying, and publishing Automaton apps.
---

# Automaton CLI

The CLI manages the local project link and sends the configured application to
Automaton. Read `automaton.json` and the generated local guidance before editing.

## Install and authenticate

```bash
dotnet tool install -g Automaton.Cli
automaton --version
automaton login
```

For unattended use, configure `AUTOMATON_API_KEY` in the process environment and
use the command's non-interactive option where available. Do not paste keys into
an AI conversation.

## Create or link

```bash
automaton new "My App"
automaton link <app-id>
```

`automaton new` creates and links a remote app. `automaton link` connects the
current project to an existing app. Both commands refresh the two local AI files.

If remote creation fails after the local file is written, run:

```bash
automaton validate
automaton create
automaton deploy
```

## Configure and deploy

Declare the generic `application` contract in `automaton.json`. It supports an
optional build command, a runtime command, an HTTP health path, durable storage,
working directories, environment values, and timeouts.

```bash
automaton validate
automaton deploy
```

The Runner performs the configured build and starts the configured runtime. The
deployment operation is asynchronous. Wait for `Succeeded`, then verify the
deployed URL.

## Other commands

```bash
automaton sync-ai
automaton pull
automaton runners
automaton publish
```

`sync-ai` restores the canonical local guidance files. `pull` downloads editable
app source. `runners` lists available Runners. `publish` makes the latest
deployment public and requires explicit user approval.

Use `automaton publish` only after deployment and approval.
