---
title: Automaton CLI reference
description: CLI commands for creating, linking, validating, deploying, and publishing Automaton apps.
---

# Automaton CLI

The CLI manages the local project link and sends the configured application to
Automaton. Read `automaton.json` and the generated local guidance before editing.

## Install and authenticate

Use the API URL from [your server installation](./install-server) or your team administrator. The NuGet CLI requires the .NET 10 SDK or later.

```bash
dotnet tool install -g Automaton.Cli
automaton --help
automaton login --server https://api.your-domain.com
```

For unattended use, configure `AUTOMATON_API_KEY` in the process environment and
use the command's non-interactive option where available. Do not paste keys into
an AI conversation.

## Install a Runner

After login, install a Machine Management Runner on an Ubuntu host with Docker running:

```bash
automaton runners install --name main --ssh user@host
```

Use `--local` on the host (the Ubuntu host needs Docker running), or `--print-command` to run the one-time command yourself before its printed expiry. Without a mode, an interactive terminal prints the command. The CLI asks you to acknowledge that the trusted Runner has Docker access on its host; `--yes` provides that acknowledgement for automation (required when not interactive). The installer command goes to SSH or sudo over standard input and is not passed as a process argument. The installer step times out after 900 seconds. The CLI waits up to 300 seconds for a connection (`--timeout` changes this), prints the Runner ID, and can add the connected Runner to a project with `--project NAME`. Use `--json` with `--ssh` or `--local` for `{runnerId, name, status}`.
## Several servers

Log in to each server once. The CLI stores each server and its key together in a named profile:

```bash
automaton login --server https://api.first.example --profile first
automaton login --server https://api.second.example --profile second
automaton profiles
automaton profiles use first
automaton --profile second whoami
automaton logout --profile second
```

Without `--profile`, login names a profile after the server host, dropping an initial `api.`. `automaton profiles` shows the server, current selection and a key fingerprint, never the key. `automaton whoami` shows the chosen profile and server. For any command, `--profile` takes precedence over `AUTOMATON_PROFILE`; otherwise an app folder chooses the profile matching the server recorded in its selected `.automaton` environment, then falls back to the current profile outside an app folder. Existing project-local credentials for that same server remain usable. If an app's server has no matching profile or local credential, log in to that server before deploying. `automaton logout` removes only the selected profile and leaves other logins intact.

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
automaton project list
automaton publish
automaton listing show
```

`sync-ai` restores the canonical local guidance files. `pull` downloads app
source. `runners` lists available Runners. `project create|list|add-app` manages projects with an API key; adding an app to a project lets it use the project secret vault. To move a deployed Live environment to another Runner, run `automaton set-runner <runner> --cleanup-runtime`, then `automaton deploy`; a published static site stays published. `publish` makes the latest deployment
public and requires explicit user approval. `listing` sets the app's marketplace
summary, description, category, tags, cover and screenshots; see
[Publish](./publish#marketplace-listing).

System administrators of a self-hosted server can update it from the CLI:
`automaton server update --wait` installs the latest release (or `--version
server-vX.Y.Z`), then checks `/health` and the installed version and exits
nonzero on failure with the log tail and recovery backup;
`automaton server update-status` shows the last run. See
[Update an installed server](https://github.com/rzvdaniel/automaton/blob/main/docs/prod/install-automaton.md#update-an-installed-server).

The optional positional argument to `automaton deploy` selects a remote
deployment target. Its `--env <name>` option instead selects a local project
profile for credentials and app linkage.

Publish when it is within the requested app delivery or an existing standing authorization. Do not ask for separate publication approval again; honor any user restriction to keep the app private or unpublished. For authorized non-interactive publication, use `automaton publish --yes` and verify the published URL.
