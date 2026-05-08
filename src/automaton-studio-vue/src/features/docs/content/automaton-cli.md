# Automaton CLI

The Automaton CLI (`automaton`) prepares local app folders, deploys apps, manages versions, and lists runners from the terminal.

The default happy path is `automaton setup`, then `automaton new` or `automaton link`, then `automaton deploy`. Lower-level auth and config commands remain available for testing, debugging, and automation.

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

## One-time setup

Prepare the current machine or workspace:

```bash
automaton setup
```

Get an API key from **Settings → API Keys → New Key** (scope: `execute`).

Manual configuration is still available when you need it:
```bash
automaton config --server https://api.automaton.monster --key ak_execute_xxx
automaton config --show
```

By default, `automaton setup` stores project-local credentials in `./.automaton/config.json`. Use `automaton setup --global` if you intentionally want shared machine-wide credentials in `~/.automaton/config.json`. Environment variables always take precedence over file-based config (useful for CI/CD):

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

### `automaton setup`

Prepare this machine or workspace for Automaton development.

```bash
automaton setup
automaton setup --global
automaton setup --server https://custom-host
```

By default, `setup` uses `https://automaton.monster`, saves credentials locally unless `--global` is used, and also syncs the generated local AI guidance pack.

---

### `automaton new`

Create a new app and link the current directory.

```bash
automaton new
automaton new "My App"
```

This creates the remote app, writes `automaton.json`, creates `.automatonignore` if missing, and syncs the local AI guidance pack.

---

### `automaton link`

Link the current directory to an existing Automaton app.

```bash
automaton link
automaton link 3fa85f64-5717-4562-b3fc-2c963f66afa6
```

This writes `automaton.json`, creates `.automatonignore` if missing, and syncs the local AI guidance pack.

---

### `automaton sync-ai`

Refresh or repair the generated local AI guidance files in the current folder.

```bash
automaton sync-ai
automaton sync-ai --json
```

Use this only when the generated `AGENTS.md`, `.github/`, or `.automaton/ai-guidance/` files are missing or stale. It is not required on the normal happy path because `setup`, `new`, and `link` already do this automatically.

---

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
# 1. Prepare the local workspace
automaton setup

# 2. Create a new app (or use link <id> to connect an existing one)
automaton new "Poker Planning"

# 3. Optional: refresh generated guidance only if needed
automaton sync-ai

# 4. Build your app locally, then deploy
automaton deploy

# 5. Snapshot before a big change
automaton publish --message "Initial release"

# 6. Pull server state if you switched machines
automaton pull
```
