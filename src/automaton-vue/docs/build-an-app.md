---
title: Build an app
description: One page for building an Automaton app that deploys the first time. A minimal automaton.json, then settings, secrets, permissions, docs and the apps repository.
order: 25
---

# Build an app

An app is a folder with an `automaton.json` and your source in any language. You rarely write it by hand: your AI does, from the [Work with AI](./work-with-ai) loop. This page is what you or your AI need to get a first deploy right.

## Create the app

Check that the CLI is signed in and a Runner is connected, then create the app in an empty folder.

```bash
automaton whoami
automaton runners
mkdir my-app && cd my-app
automaton new "My App" --template node
```

`--template` is `node`, `python`, `dotnet` or `static`. `new` writes `automaton.json`, `AGENTS.md` and the app skill, and creates the app on the server. For an app that already exists, run `automaton link <app-id>`. Without a template, `automaton deploy` is refused until you declare a runtime command, a static site or an Action.

## The smallest app

Two files. This deploys as written.

`automaton.json`:

```json
{
  "name": "Hello",
  "application": {
    "runtime": {
      "image": "node:22-slim",
      "command": { "executable": "node", "arguments": ["server.mjs"] },
      "port": 8080
    },
    "health": { "path": "/health" },
    "storage": { "dataDirectory": "data" },
    "settings": [
      { "name": "GREETING", "description": "Text the page shows", "default": "Hello" }
    ],
    "secrets": [
      { "name": "HELLO_KEY", "description": "Example secret" }
    ]
  }
}
```

`server.mjs`:

```js
import http from "node:http";

const port = process.env.AUTOMATON_PORT || 8080;
http.createServer((req, res) => {
  if (req.url === "/health") return res.end("ok");
  const key = process.env.HELLO_KEY ? "set" : "missing";
  res.end(`${process.env.GREETING || "Hello"} (secret ${key})\n`);
}).listen(port);
```

Then set the secret, validate and deploy:

```bash
echo -n "a-value" | automaton secrets set HELLO_KEY
automaton validate
automaton deploy
automaton apps
```

`deploy` can print `Deploy complete` before the app runs. Wait until `automaton apps` shows `Running`. If it does not, read `automaton logs`.

What the Runner gives your app:

| Variable | Meaning |
|---|---|
| `AUTOMATON_PORT` | The port to listen on. Never hard-code one. |
| `AUTOMATON_APP_DATA_DIR` | The folder that survives redeploys. Everything else is replaced on each deploy. |
| `AUTOMATON_API_URL` | The server's API address. |

The process must answer `health.path` with a 2xx response. Server-Sent Events and WebSockets work; an idle one closes after five minutes, so send a heartbeat.

## A static site

No server process: a build writes files into a folder. `automaton new "My Site" --template static` creates it.

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

The health file is `health.txt` because only public file types are published. Static sites cannot use secrets: their files go to the browser.

## Settings

A setting is a plain, non-secret value with an optional `default`, filled in by whoever installs the app and passed as an environment variable. Add `"required": true` to make the installer answer it. Anything sensitive is a secret instead.

```json
"settings": [{ "name": "S3_REGION", "description": "Object storage region", "default": "us-east-1" }]
```

A changed setting restarts the running app. A default can use the app's own public address; see [Manifest fields](#manifest-fields).

## Secrets

Declare the name in `application.secrets`, set the value from standard input (never from an argument, so it stays out of shell history and AI chat). Saving or deleting an app secret restarts the running app a few seconds later (several changes close together give one restart) and the app's activity says "Restarted to apply secret NAME". Vault secrets (`--project`, `--user`) are not restarted for yet: redeploy to pick them up. Read it from the environment in code.

```bash
automaton secrets set STRIPE_KEY              # this app
automaton secrets set SHARED_KEY --project    # shared by the apps of a project
automaton secrets set MY_KEY --user           # your own vault, for personal apps
automaton secrets status                      # present or missing, and where from
automaton deploy
```

You can also set them in Studio under the app's **Settings → Secrets**. Values are encrypted and never shown again; saving a name again replaces it. `"optional": true` lets an installer leave one empty; `automaton validate --strict` fails when a required one is missing. 

## Permissions

An app gets no access to the server by default. To read server health, list backups or send email, declare it:

```json
"permissions": ["server:read", "notify:email"]
```

Deploy, then run `automaton permissions status`. A system administrator approves each request in Studio (the Home page lists them with Approve and Reject; the command prints the link). The app then receives a scoped key as `APP_AUTOMATON_API_KEY` at its next start. Nothing is granted before that, and revoking stops the next call. Permissions the platform knows include `server:read`, `backups:read`, `backups:run`, `server:restart`, `server:update-os`, `server:cleanup`, `security:read`, `security:manage` and `notify:email`; `automaton permissions status` explains each.

### Email people from an app

With `notify:email` approved, the app asks the server to send mail through the server's own SMTP and never sees its credentials.

```bash
curl -X POST "$AUTOMATON_API_URL/api/notify/email" \
  -H "Authorization: Bearer $APP_AUTOMATON_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"to":["administrators"],"subject":"Disk almost full","text":"Only 2 GB left."}'
```

`to` takes `owner`, `administrators`, `project` and addresses an administrator put on the server's allowed list; any other address gets 403. The subject is prefixed with the app name and ends with the server's host, so mail from a test server is never mistaken for production's. An app can send 10 emails an hour (429 beyond that). Answers: 200 `{"sent":n}`, 400 invalid request, 403 not approved or not allowed, 404 no recipient, 409 SMTP not configured. Every send is in Admin → Audit Events.

For actions that need an administrator (`server:cleanup`, `backups:run`, `server:restart`, `server:update-os`, `security:manage`), the app receives `X-Automaton-Viewer-Role` (to hide controls) and a short-lived `X-Automaton-Viewer-Token` on each request, and forwards the token on its calls. The API answers 403 unless the viewer is a system administrator. The proxy removes both headers from client requests, so you can trust them when present.

## Give the app its own docs

Put a `docs/` folder next to `automaton.json` with `docs/README.md` as the entry page (the Docs tab opens a `README.md` in the app folder first, if there is one). Other `.md` pages link with relative links, and images (png, jpg, svg, webp, up to 5 MB) show with `![Alt](plans.png)`. `automaton deploy` uploads it with the source, so each version shows its own docs in the app's **Docs** tab and through `automaton docs <app>`. The catalog's install page shows the same README before someone installs. Update it in the same change as the app.

## Put it in an apps repository

An apps repository is a GitHub repository whose apps appear in **Apps → Catalog** for others to install.

```bash
automaton catalog init my-apps
cd my-apps
git init -b main && git add . && git commit -m "First apps repository"
```

Push it to GitHub, wait for the `Catalog` workflow to write `catalog.json`, then a system administrator adds the repository under **Apps → Catalog → Catalog sources**. Each app folder needs `name` and `summary` in its `automaton.json`. Installing makes the installer's own copy; permissions are never granted by the catalog. The format is under [Catalog files](#catalog-files).

## Common errors

- **Build fails with "No space left on device".** The build container's `/tmp` is a few tens of MB. Unpack and cache inside the app folder and point `HOME` and `TMPDIR` there.
- **Build tools exist locally but not on the Runner.** Set `application.build.image`.
- **`Template would overwrite files`.** Use an empty folder or omit `--template`.
- **Login does nothing.** The server address must be the API address, not Studio.
- **Deploy works, app not `Running`.** Read `automaton logs`: the app must listen on `AUTOMATON_PORT` and answer its health path.
- **Secret shows `missing`.** Set it in the right scope; `--project` needs `automaton project add-app` first.
- More: [When something goes wrong](./when-something-goes-wrong). The field list is [below](#manifest-fields); publishing is in [CLI recipes](./cli#publish-pause-and-delete).

## Manifest fields

All fields sit under `application` unless noted. `appId`, `name` and `summary` (one line for the store card, at most the first screen) are top level; `server` optionally names the server.

| Field | Meaning |
|---|---|
| `build.command`, `build.image` | Build executable, `arguments`, `workingDirectory`, `environment`; the image defaults to `runtime.image`. |
| `build.outputDirectory`, `build.timeoutSeconds` | Folder holding the build result; maximum build time. |
| `build.singlePageApp` | Static sites only (default `false`). `true` serves `index.html` for unknown paths; otherwise a missing path is 404. |
| `runtime.image`, `runtime.command`, `runtime.port` | Image, long-running process and port. The image is required with a command. |
| `runtime.user` | Numeric UID or UID:GID; keep it stable if the app updates files it created. |
| `runtime.update` | `side-by-side` (default; the old container serves until the new one is ready) or `stop-first` for apps that lock their data folder. |
| `health.path`, `health.startupTimeoutSeconds` | Ready after a 2xx answer; maximum start time. |
| `storage.dataDirectory`, `storage.mounts` | Folders that survive redeploys. |
| `kind` | `web` (default) or `api`. |
| `description`, `pages`, `openApi` | One line (200 characters), up to 10 key paths `{ "path": "/", "label": "Home" }`, and an API app's OpenAPI path such as `/openapi.json`. |
| `homeCard` | `{ "intervalSeconds": 900 }` (300 to 86400): how often the `home-card` Action refreshes the app's card on Studio Home. |
| `openPaths` | Paths that answer without Automaton sign-in (see below). |
| `publicAccess` | Hint `{ "level": "required" or "recommended", "reason": "..." }` shown before install when outside callers must reach the app. It changes nothing by itself. |
| `settings`, `secrets`, `permissions` | Described above. |

The format is an open spec with JSON Schemas for `automaton.json` and `catalog.json`: [automaton.monster/spec](https://automaton.monster/spec). To start from a Docker Compose file, run `automaton import-compose compose.yaml --out my-app`: it writes `automaton.json` and a `compose.yaml` reduced to what Automaton runs (a published image per service, one routed HTTP port, named volumes), lists what it dropped, and refuses `build:`, host mounts and `$` interpolation.

Commands take structured arguments; set `useShell` only when you need a shell. Keep paths inside the project folder. Deployment is asynchronous: wait for `Running` in `automaton apps`.

### Open paths

The private address asks every visitor to sign in, so an outside service (a payment webhook, an OAuth callback) cannot reach the app. Declare the paths it calls:

```json
"application": { "openPaths": ["/hooks/*", "/oauth/callback"] }
```

`/hooks/*` opens `/hooks/` and everything below it. Name at least one folder or file (`/` and `/*` are refused), use only letters, digits and `/ - _ . ~`, at most 20 paths. Those paths answer anyone who knows the address, so the app must check the caller itself (a signature or secret). The routes update with the next deploy.

### Start an Action from outside

A webhook lets something outside (a monitor, a GitHub release) start an Action. Create one with `automaton action webhook add <action-key> --name "Docs walk"` or **Add webhook** on the app's **Actions** tab. The secret is shown once; `rotate` replaces it, `list` and `delete` manage them.

```bash
curl -X POST https://api.example.com/api/hooks/<webhook-id> \
  -H "Authorization: Bearer <secret>" -H "Content-Type: application/json" \
  -d '{"version": "0.2.175"}'
```

The JSON body (up to 64 KB) is the Action's input. A wrong secret gets 404, a disabled webhook 403, a paused app or an Action already running 409, and more than 30 calls a minute 429. Schedules use five-field cron plus a time zone: `automaton action schedule add <action> --cron "0 2 * * 1-5" --tz Europe/Bucharest`; `list`, `edit`, `on`, `off` and `delete` manage them.

## Catalog files

Layout of an apps repository:

```text
my-apps/
  catalog.json            generated; the index a server reads
  catalog-apps.json       the apps you want listed
  apps/hello/automaton.json
  .github/workflows/catalog.yml
```

`catalog-apps.json` lists the apps:

```json
{ "repo": "owner/name", "apps": [ { "folder": "apps/hello", "category": "Examples", "minServerVersion": "0.2.32" } ] }
```

`folder` (path from the repository root), `category` and `minServerVersion` (older servers show the entry without Install) are required. Optional: `repo` (defaults to the GitHub Actions repository), `publisher` (`{ "name": "Acme", "logo": "https://…" }`) and `movedFrom` (`{ "repo": "...", "folder": "..." }` for an app that left another catalog). The workflow writes `catalog.json` from these and from each app's `name`, `summary` and `permissions`. Manage sources from a terminal with `automaton catalog --help`. A catalog entry may also carry an `icon` and up to eight `screenshots`, as paths inside the app folder (png, jpg, webp or svg, up to 512 KB each) that the server loads from the commit the entry is pinned to; Studio's catalog shows the icon, or a letter when there is none. The official apps repository's generator reads them from each app's `automaton.json` (`"icon": "icon.svg"`); the `catalog init` workflow does not add them yet. Run `automaton validate` in each app folder first.

## Related

[Back up and restore](./backup-restore) and the [Security overview](./security) are reference pages.
