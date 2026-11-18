---
title: Automaton CLI reference
description: CLI commands for creating, linking, validating, deploying, and publishing Automaton apps.
order: 120
---

# Automaton CLI

The CLI manages the local project link and sends the configured application to
Automaton. Read `automaton.json` and the generated local guidance before editing.

## Install and authenticate

Use the API URL from [your server installation](./install-server) or your team administrator. The CLI is a single file that needs no .NET. On Linux or macOS:

```bash
curl -fsSL https://github.com/rzvdaniel/automaton/releases/latest/download/install.sh | sh
```

On Windows (PowerShell): `irm https://github.com/rzvdaniel/automaton/releases/latest/download/install.ps1 | iex`. The script puts `automaton` in `~/.local/bin` (Windows: `%LOCALAPPDATA%\Automaton\bin`) and checks its SHA-256. The script always installs the latest release unless you set `AUTOMATON_VERSION=server-v<version>`; a newer CLI works with an older server.

If `automaton whoami` already names your server and user, skip login. If the CLI is older than the server (a command or option is missing), rerun the install script above, or `dotnet tool update -g Automaton.Cli`; check with `automaton --version`.

Already have the .NET 10 SDK? `dotnet tool install -g Automaton.Cli` installs the same CLI.

```bash
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

An agent that just created the administrator skips the two `read` prompts: `EMAIL` and `PASSWORD` are already set in the same shell; it runs the script, then `unset`s them. Never print them.

`POST /api/Account/LoginUser` with `{"email","password"}` returns JSON whose `accessToken` goes in `Authorization: Bearer`; do not rely on a cookie. The approve body is `{"userCode": "..."}`.

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

`automaton runners upgrade RUNNER [--wait]` upgrades an online Runner to the server's current image (non-zero exit if it rolled back); `automaton server update --runners` (it implies `--wait`) names every Runner with its state (current or on an old image) and upgrades the old ones; `--wait` alone only lists them with the upgrade command. `automaton runners install --name NAME --ssh user@host | --local | --print-command [--yes] [--project NAME]`. `automaton runners delete RUNNER` removes one, including a Runner that is offline for good (`--abandon-offline --yes`): see [Remove a Runner](./deploy-runner#remove-a-runner). Steps, options and errors: [Add a Runner](./deploy-runner).

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
automaton new "My App" --template node   # or python, dotnet, static
automaton link <app-id-or-slug>
```

`automaton new --template ...` creates a local `automaton.json` with a runtime command, creates a remote app,
and records its ID in the selected local profile. It also syncs the generated
AI guidance. `automaton link` requires an existing valid `automaton.json` and
records the remote app ID in the selected profile without downloading or
changing local source or guidance files. After linking, deploy the local source
with `automaton deploy`. Without `--template`, `automaton new` writes an empty
contract and `automaton deploy` fails with "runtime command is required".

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
automaton apps                  # your apps: ID, name, Runner, status (Paused when paused), public URL
automaton installation-status   # server version, counts, last backup
automaton notifications         # the same notices as Studio Home; add --json for scripts and agents
automaton notifications acknowledge <key>   # hide one (critical notices stay until fixed)
automaton runners
automaton project runners       # list, add or remove a project's Runners
automaton project list
automaton publish --yes --slug NAME
automaton share status           # who can open the app: me, people, a team, server or internet (same choices as the Share dialog)
automaton share set server       # or: me, people, team --team NAME, internet --slug NAME [--list|--no-list] (store listing text: automaton listing); add/remove EMAIL manage "people"
automaton pause --reason "why"  # (reason optional; the owner sees it) stop the linked app, switch its schedules off, show a paused page (also: automaton apps pause APP)
automaton resume                # start it again and turn its schedules back on (also: automaton apps resume APP); automaton deploy --resume resumes first
automaton delete --yes          # permanently delete the linked remote app
automaton apps delete <name-or-id> --yes   # delete any app you may delete, no linked folder needed (for example a catalog-installed app)
automaton listing show
automaton catalog init my-apps  # files for your own apps repository (writes only; no git, no GitHub)
automaton catalog build         # generate catalog.json from catalog-apps.json
automaton catalog list          # apps in the server's catalog: id, source, permissions
automaton catalog install ID --name NAME --runner RUNNER --setting NAME=VALUE --secrets-file FILE --wait
automaton catalog update APP --check   # is there a newer commit? (exit 2 = yes); without --check it updates and redeploys
```

`sync-ai` restores the canonical local guidance files. `pull` downloads app
source. `runners` lists available Runners. `project create|list|add-app` manages projects with an API key; adding an app to a project lets it use the project secret vault. To move a deployed Live environment to another Runner, run `automaton set-runner <runner> --cleanup-runtime`, then `automaton deploy`; a published static site stays published. `publish` makes the latest deployment
public and requires explicit user approval. `listing` sets the app's marketplace
summary, description, category, tags, cover and screenshots; see
[Deploy and publish an app](./deploy#marketplace-listing).

`catalog list` and `catalog install` do from the terminal what **Apps → Catalog → Install** does: `list` shows every entry with its id, source and requested permissions (`--search`, `--category`, `--source`, `--installed`, `--json`; without a terminal, as in a script or an AI agent, the table wraps at 80 columns, so use `--json`); `install <id>` prints the pinned commit and the permissions the app requests, creates the app, saves its settings and secrets and deploys it (`--wait` reports whether the deployment succeeded). Give settings as `--setting NAME=VALUE` (repeat the option). Secrets are never arguments: pass `NAME=VALUE` lines in a file only you can read (`--secrets-file`) or on standard input (`--secrets-stdin`), or run the command in a terminal and type each one at a hidden prompt. The command stops before creating anything when a required setting or secret is missing, and says which. Permissions still need a system administrator's approval in Studio. If two catalog sources have the same id, add `--source <id>`. `catalog update <app>` (app id or slug) does what Studio's Update button does: it shows the installed and pinned commits, the compare link and new settings, secrets and permissions, then replaces the files and redeploys (`--wait`, `--json`). Files edited after the install need `--confirm-edited`; new secrets come from `--secrets-file` or `--secrets-stdin`.

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
| `automaton action` | Validate and run Actions in the linked App |
| `automaton secrets` | Check and fill the secrets this app declares in automaton.json (application.secrets) |
| `automaton project` | Create projects, list yours and add the current app to one |
| `automaton permissions` | Show the platform permissions this app requests and whether an administrator approved them |
| `automaton init` | Create a new Automaton app or link an existing one to this directory |
| `automaton link <app-id-or-slug>` | Link this directory to an existing Automaton app |
| `automaton deploy <target>` | Build, upload, and deploy the app to Automaton |
| `automaton open <environment>` | Open the current app in the browser |
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
