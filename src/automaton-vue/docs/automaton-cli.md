---
title: Automaton CLI reference
description: CLI commands for creating, linking, validating, deploying, and publishing Automaton apps.
---

# Automaton CLI

The CLI manages the local project link and sends the configured application to
Automaton. Read `automaton.json` and the generated local guidance before editing.

## Install and authenticate

Use the API URL from [your server installation](./install-server) or your team administrator. The NuGet CLI requires the .NET 10 SDK or later. Ubuntu 22.04 and Debian 12 have no .NET 10 package in apt; install the SDK per user:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 10.0
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$PATH:$HOME/.dotnet:$HOME/.dotnet/tools"
```

Add the two `export` lines to your shell profile so `automaton` is found in new sessions.

```bash
dotnet tool install -g Automaton.Cli
automaton --help
automaton login --server https://api.your-domain.com
```

`automaton login` prints the approval URL and a code, then notices your approval within about five seconds. On a server without a browser it prints the URL; open it on another device. Use `--device` to skip the browser attempt.

## Log in without a browser

On a server over SSH, or for an AI agent, use one of these. Neither needs a browser on the machine that runs the CLI.

**Device approval.** Run `automaton login --device --server https://api.your-domain.com`. It prints an approval URL and a code, and waits, so run it in the background and keep its output in a private file (it contains the code). Approve it from any signed-in session: open the URL in a browser on another device, or approve it from a terminal with an administrator or user token. The email and password are the account's own sign-in; for the first administrator that is the email chosen at [setup](./first-admin).

```bash
API=https://api.your-domain.com
OUT=$(umask 077; mktemp)   # private file (mode 600) for the --device output
automaton login --device --server "$API" > "$OUT" 2>&1 &
for _ in $(seq 20); do grep -q 'cli/authorize' "$OUT" && break; sleep 1; done   # wait for the URL
# The output has a line: https://studio.../cli/authorize?requestId=REQUEST_ID&code=USER_CODE
URL=$(grep -o 'https://[^ ]*cli/authorize?[^ ]*' "$OUT" | head -1)
REQUEST_ID=${URL#*requestId=}; REQUEST_ID=${REQUEST_ID%%&*}
USER_CODE=${URL#*code=}; USER_CODE=${USER_CODE%%&*}
read -rp "Email: " EMAIL; read -rsp "Password: " PASSWORD; echo   # no terminal: PASSWORD=$(< /root/admin-password), a 0600 file; needs jq
TOKEN=$(jq -n --arg e "$EMAIL" --arg p "$PASSWORD" '{email:$e,password:$p}' |
  curl -s -X POST "$API/api/Account/LoginUser" -H 'Content-Type: application/json' --data-binary @- | jq -er .accessToken)
curl -s -o /dev/null -w '%{http_code}\n' -X POST "$API/api/cli-auth/requests/$REQUEST_ID/approve" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "$(jq -n --arg c "$USER_CODE" '{userCode:$c}')"
unset PASSWORD TOKEN
wait; tail -n 3 "$OUT"; rm -f "$OUT"   # "Signed in as ..." once approved. If the code was refused, stop the wait with Ctrl+C or `kill %1`.
```

The code is letters, digits and a dash (for example `ABCD-1234`), so it needs no URL decoding. `200` means approved; `401` means the sign-in failed, and `403` that the account's email is not confirmed. The waiting CLI notices within about five seconds. The `--device` output contains the approval code: do not share it. Codes expire after a few minutes, so approve soon after `login` prints it.

**API key.** Create a key in Studio (**API Keys**) or with `POST /api/ApiKeys` (`{"name": "my-cli"}`, bearer token as above; the response `apiKey` is shown once). Then:

```bash
automaton login --server https://api.your-domain.com --key
```

`--key` with no value asks for the key at a hidden prompt. Do not put the key in a command argument, a log or an AI conversation; for unattended jobs set `AUTOMATON_SERVER` and `AUTOMATON_API_KEY` in the process environment instead.

For unattended use, configure `AUTOMATON_API_KEY` in the process environment and
use the command's non-interactive option where available. Do not paste keys into
an AI conversation.

## Install a Runner

`automaton runners upgrade RUNNER [--wait]` upgrades an online Runner to the server's current image (non-zero exit if it rolled back); `automaton server update --runners` (it implies `--wait`) names every Runner with its state (current or on an old image) and upgrades the old ones; `--wait` alone only lists them with the upgrade command. `automaton runners install --name NAME --ssh user@host | --local | --print-command [--yes] [--project NAME]`. Steps, options and errors: [Add a Runner](./deploy-runner).

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

Without `--profile`, login names a profile after the server host, dropping an initial `api.`. `automaton profiles` shows the server, current selection and a key fingerprint, never the key. `automaton whoami` shows the chosen profile and server. For any command, `--profile` takes precedence over `AUTOMATON_PROFILE`; otherwise an app folder chooses the profile matching the server recorded in its selected `.automaton` environment, then falls back to the current profile outside an app folder. Existing project-local credentials for that same server remain usable. If an app's server has no matching profile or local credential, log in to that server before deploying. `automaton logout` removes only one profile and leaves other logins intact: the `--profile` or `AUTOMATON_PROFILE` selection, else (inside an app folder) the profile matching the app's server, else the current profile. After the current profile is removed no profile is current until you run `automaton profiles use NAME` or log in again; the CLI never switches silently.

`--profile` (or `AUTOMATON_PROFILE`) inside an app folder whose server differs from the profile's is an error ("This app is on ..."). It cannot be combined with `AUTOMATON_SERVER` or `AUTOMATON_API_KEY`; without a profile those variables still override stored credentials. A project-local key must be stored with its server (`automaton login --local --server URL`); a key without a server is refused. `automaton config --global --server URL` saves (or updates) the profile named after that server instead of a single global server.

The shared file `~/.automaton/config.json` changed format (version 2, with a `profiles` map and the `current` profile). The CLI upgrades an older file on first use, keeping its credentials. Older CLI versions cannot read the new format, so update them everywhere you use the file: `dotnet tool update -g Automaton.Cli`. Set `AUTOMATON_CONFIG_DIR` to keep the file in another directory (`$AUTOMATON_CONFIG_DIR/config.json`); the CLI test suite uses it so tests never touch your real login.

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
automaton apps                  # your apps: ID, name, Runner, status, public URL
automaton installation-status   # server version, counts, last backup
automaton runners
automaton project runners       # list, add or remove a project's Runners
automaton project list
automaton publish --yes --slug NAME
automaton share status           # who can open the app: me, people, a team, server or internet (same choices as the Share dialog)
automaton share set server       # or: me, people, team --team NAME, internet --slug NAME [--list|--no-list] (store listing text: automaton listing); add/remove EMAIL manage "people"
automaton delete --yes          # permanently delete the linked remote app
automaton apps delete <name-or-id> --yes   # delete any app you may delete, no linked folder needed (for example a catalog-installed app)
automaton listing show
automaton catalog init my-apps  # files for your own apps repository (writes only; no git, no GitHub)
automaton catalog build         # generate catalog.json from catalog-apps.json
automaton catalog list          # apps in the server's catalog: id, source, permissions
automaton catalog install ID --name NAME --runner RUNNER --setting NAME=VALUE --secrets-file FILE --wait
```

`sync-ai` restores the canonical local guidance files. `pull` downloads app
source. `runners` lists available Runners. `project create|list|add-app` manages projects with an API key; adding an app to a project lets it use the project secret vault. To move a deployed Live environment to another Runner, run `automaton set-runner <runner> --cleanup-runtime`, then `automaton deploy`; a published static site stays published. `publish` makes the latest deployment
public and requires explicit user approval. `listing` sets the app's marketplace
summary, description, category, tags, cover and screenshots; see
[Deploy and publish an app](./deploy#marketplace-listing).

`catalog list` and `catalog install` do from the terminal what **Apps → Catalog → Install** does: `list` shows every entry with its id, source and requested permissions (`--search`, `--category`, `--source`, `--installed`, `--json`); `install <id>` prints the pinned commit and the permissions the app requests, creates the app, saves its settings and secrets and deploys it (`--wait` reports whether the deployment succeeded). Give settings as `--setting NAME=VALUE` (repeat the option). Secrets are never arguments: pass `NAME=VALUE` lines in a file only you can read (`--secrets-file`) or on standard input (`--secrets-stdin`), or run the command in a terminal and type each one at a hidden prompt. The command stops before creating anything when a required setting or secret is missing, and says which. Permissions still need a system administrator's approval in Studio. If two catalog sources have the same id, add `--source <id>`.

`catalog init` and `catalog build` create and maintain an apps repository that servers can use as a catalog source: see [Publish your own app catalog](./apps-repository).

System administrators of a self-hosted server can update it from the CLI:
`automaton server update --wait` installs the latest release (or `--version
server-vX.Y.Z`), then checks `/health` and the installed version and exits
nonzero on failure with the log tail and recovery backup (an older release needs `--allow-downgrade`);
`automaton server update-status` shows the last run. See
[Update an installed server](./update-server).

The optional positional argument to `automaton deploy` selects a remote
deployment target. Its `--env <name>` option instead selects a local project
profile for credentials and app linkage.

Publish when it is within the requested app delivery or an existing standing authorization. Do not ask for separate publication approval again; honor any user restriction to keep the app private or unpublished. For authorized non-interactive publication, use `automaton publish --yes` and verify the published URL.
