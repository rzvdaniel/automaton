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
automaton link <app-id-or-slug>
```

`automaton new` creates a valid local `automaton.json`, creates a remote app,
and records its ID in the selected local profile. It also syncs the generated
AI guidance. `automaton link` requires an existing valid `automaton.json` and
records the remote app ID in the selected profile without downloading or
changing local source or guidance files. After linking, deploy the local source
with `automaton deploy`.

`automaton pull <app-id-or-slug>` creates a new local project from portable
remote source. App IDs work for accessible apps; exact public slugs are resolved
from the apps visible to your account, so project apps may require an app ID.
Pulling into an existing linked project warns before overwriting matching files
and stages downloads before exposing them locally.

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

`sync-ai` restores the canonical local guidance files. `pull` downloads app
source. `runners` lists available Runners. `publish` makes the latest deployment
public and requires explicit user approval.

The optional positional argument to `automaton deploy` selects a remote
deployment target. Its `--env <name>` option instead selects a local project
profile for credentials and app linkage.

Publish when it is within the requested app delivery or an existing standing authorization. Do not ask for separate publication approval again; honor any user restriction to keep the app private or unpublished. For authorized non-interactive publication, use `automaton publish --yes` and verify the published URL.
