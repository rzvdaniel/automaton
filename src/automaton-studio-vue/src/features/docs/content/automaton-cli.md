# Automaton CLI

The Automaton CLI (`automaton`) deploys apps, manages versions, and lists runners from the terminal.

It also supports explicit login/logout flows for local development, testing, and account switching.

---

## Requirements

- **.NET 9 runtime** — download from [https://dot.net](https://dot.net)

---

## Install

**macOS / Linux — installer script:**
```bash
curl -fsSL https://api.automaton.monster/downloads/install.sh | bash
```

**Windows — PowerShell installer:**
```powershell
irm https://api.automaton.monster/downloads/install.ps1 | iex
```

**Via .NET global tool (all platforms):**
```bash
dotnet tool install -g Automaton.Cli
```

Verify the install:
```bash
automaton --version
```

---

## One-time configuration

Store your server URL and API key globally so every project can use them:

```bash
automaton config --server https://api.automaton.monster --key ak_execute_xxx
```

Get an API key from **Settings → API Keys → New Key** (scope: `execute`).

Check what is currently configured:
```bash
automaton config --show
```

Configuration is stored in `~/.automaton/config.json`. Environment variables always take precedence over the config file (useful for CI/CD):

| Variable | Purpose |
|---|---|
| `AUTOMATON_SERVER` | Server URL |
| `AUTOMATON_API_KEY` | API key |

---

## `automaton.json`

Every project has an `automaton.json` at the root. Commit it to Git.

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My App"
}
```

For framework apps (React, Vue, Angular) that need a build step:

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My App",
  "build": "npm run build",
  "deployDir": "dist"
}
```

| Field | Required | Description |
|---|---|---|
| `appId` | ✅ | App ID from Automaton Studio |
| `name` | ✅ | Display name |
| `build` | Optional | Shell command to run before upload (e.g. `npm run build`) |
| `deployDir` | Optional | Directory to upload as frontend (default: tries `dist/`, `frontend/`, then root) |

---

## Commands

### `automaton login`

Store and validate CLI credentials for the current project by default.

```bash
automaton login
automaton login --global
automaton whoami
```

By default, credentials are saved to `./.automaton/config.json`. Use `--global` to save them to `~/.automaton/config.json` instead.

---

### `automaton logout`

Clear stored CLI credentials.

```bash
automaton logout           # clear project-local credentials
automaton logout --global  # clear global credentials
automaton logout --all     # clear both
```

Use this when testing first-run auth flows, switching accounts, or resetting machine-local stored credentials. If `AUTOMATON_SERVER` or `AUTOMATON_API_KEY` are still set in the environment, they will continue to override file-based config.

---

### `automaton whoami`

Show the current authenticated user and resolved server.

```bash
automaton whoami
automaton whoami --json
```

Useful after `automaton login` or `automaton logout` to confirm which credentials are currently active.

---

### `automaton init`

Create a new app and write `automaton.json`, or link the current directory to an existing app.

```bash
# Create a new app (prompts for name if not provided)
automaton init --name "My App"

# Link to an existing app by ID
automaton init --app 3fa85f64-5717-4562-b3fc-2c963f66afa6
```

Use `automaton init --app <id>` when you have already created the app in the Studio UI and just want to connect a local folder to it.

---

### `automaton deploy`

Build (if configured), zip the project, upload to Automaton, and deploy backend Python scripts.

```bash
automaton deploy                   # standard deploy
automaton deploy --clean           # full replace — wipes existing server files first
automaton deploy --skip-backend    # upload files only, skip backend deploy step
automaton deploy --dir dist        # override the upload directory
```

What happens:
1. Runs the `build` command from `automaton.json` if set
2. Zips the `deployDir` (or the default resolution order: `dist/` → `frontend/` → root)
3. Uploads the zip to the server (merge by default, full replace with `--clean`)
4. Deploys backend Python controllers to the runner (unless `--skip-backend`)

---

### `automaton pull`

Download all app files from the server to the local directory.

```bash
automaton pull                  # download to current directory
automaton pull --dir ./local    # download to a specific directory
```

`automaton.json` is excluded from pull — it is local-only config.

---

### `automaton publish`

Save a version snapshot of the app's current server-side files. Useful before a major change.

```bash
automaton publish
automaton publish --message "Add vote reveal feature"
```

---

### `automaton runners`

List all runners registered on your account and their connection status.

```bash
automaton runners
```

---

### `automaton config`

View or update global CLI settings.

```bash
automaton config --server https://api.automaton.monster --key ak_execute_xxx
automaton config --show
```

---

## CI/CD usage

Set environment variables in your pipeline instead of storing credentials in the repo:

```yaml
# GitHub Actions example
- name: Deploy to Automaton
  env:
    AUTOMATON_SERVER: https://api.automaton.monster
    AUTOMATON_API_KEY: ${{ secrets.AUTOMATON_API_KEY }}
  run: automaton deploy
```

---

## Typical workflow

```bash
# 1. One-time: configure server + API key
automaton config --server https://api.automaton.monster --key ak_execute_xxx

# 2. Create a new app (or use init --app <id> to link existing)
automaton init --name "Poker Planning"

# 3. Build your app locally, then deploy
automaton deploy

# 4. Snapshot before a big change
automaton publish --message "Initial release"

# 5. Pull server state if you switched machines
automaton pull
```
