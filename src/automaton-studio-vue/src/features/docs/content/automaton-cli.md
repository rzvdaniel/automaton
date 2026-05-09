# Automaton CLI

The Automaton CLI (`automaton`) prepares local app folders, deploys apps, manages versions, and lists runners from the terminal.

The default user flow is AI-first: run `automaton setup`, then let your AI tool handle the app workflow in the current folder. The direct CLI commands on this page are the manual fallback and the low-level reference for testing, debugging, and automation.

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

**Via .NET global tool (all platforms, preferred when .NET 9 is available):**
```bash
dotnet tool install -g Automaton.Cli
```

Verify the install:
```bash
automaton --version
```

If `.NET 9` is not available or global tools are not practical in this environment, use the platform installer script or the portable downloads instead.

---

## One-time setup

Prepare the current machine or workspace:

```bash
automaton setup
```

Get an API key from **Settings → API Keys → New Key** (scope: `execute`).

Manual configuration is still available when you need it:
```bash
automaton config --key ak_execute_xxx
automaton config --show
```

By default, `automaton setup` stores project-local credentials in `./.automaton/config.json` and uses the hosted Automaton server automatically. Environment variables are mainly for CI/CD and internal development; normal users do not need them for the standard flow.

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
| `deployDir` | Optional | Directory to upload as frontend (default resolution checks `dist/`, then root, then `frontend/` as a compatibility fallback; for plain HTML/CSS/JS apps prefer root-level files unless you explicitly want split structure) |

---

## Commands

### `automaton setup`

Prepare this machine or workspace for Automaton development.

```bash
automaton setup
```

By default, `setup` uses the hosted Automaton server, saves credentials locally for the current project, and also syncs the generated local AI guidance pack.

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

Use this only when the generated `.automaton/agents/`, `.automaton/ai-guidance/`, `AGENTS.md`, `CLAUDE.md`, or `.github/` files are missing or stale. It is not required on the normal happy path because `setup`, `new`, and `link` already do this automatically.

---

### `automaton login`

Store and validate CLI credentials directly. Most users should prefer `automaton setup` instead.

```bash
automaton login
automaton whoami
```

By default, credentials are saved to `./.automaton/config.json` for the current project. Treat this as a lower-level fallback when you intentionally want to manage credentials outside the normal `setup` flow.

---

### `automaton logout`

Clear stored CLI credentials.

```bash
automaton logout           # clear project-local credentials
automaton logout --all     # clear both local and global credentials
```

Use this when testing first-run auth flows, switching accounts, or resetting machine-local stored credentials.

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
2. Zips the `deployDir` (or the default resolution order: `dist/` → root → `frontend/` compatibility fallback)
3. Uploads the zip to the server (merge by default, full replace with `--clean`)
4. Deploys backend Python controllers to the runner (unless `--skip-backend`)

If `automaton validate` already passes but deploy fails during upload with transport-style errors like closed stream, multipart/request stream, socket, timeout, or HTTP pipeline failures, treat that as a likely CLI or server upload-path issue first instead of immediately restructuring the app.

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
