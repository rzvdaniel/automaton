# Workflow: External IDE + CLI + AI tools

Use this path when you want to stay in Cursor, Claude Code, Copilot Chat, or another tool while keeping the project local.

## Prerequisites

### 1. Install the Automaton CLI

**macOS / Linux:**
```bash
curl -fsSL https://api.automaton.monster/downloads/install.sh | bash
```

**Windows (PowerShell):**
```powershell
irm https://api.automaton.monster/downloads/install.ps1 | iex
```

**Via .NET global tool (requires .NET 9):**
```bash
dotnet tool install -g Automaton.Cli
```

If `.NET 9` is already available, prefer the global tool because AI tools can usually install, update, and verify it more easily. If `.NET 9` is not available, use the platform installer script or portable download instead.

### 2. Prepare the current machine or workspace

```bash
automaton setup
```

Get an API key from **API Keys → Generate New Key** (scope: `execute`).

For the full CLI reference — all commands, options, `automaton.json` fields, and CI/CD usage — see the [Automaton CLI](/docs/automaton-cli) page.

---

## Recommended flow

### AI-first flow

1. Install the CLI once.
2. Run `automaton setup` in the app folder you want to create or manage.
3. Open your preferred AI tool in the app folder.
4. Ask it to build and deploy the app to Automaton Studio.

Each app should have its own `automaton.json` and local guidance scaffold. After `automaton setup`, the AI should be able to handle most of the remaining work in the local project: scaffold or edit files, create or link the app, keep `automaton.json` correct, run deploy preflight, handle runner selection for backend apps, and deploy. The user should usually only need to step in for interactive auth, runner creation outside the CLI, or publish approval.

### Manual fallback

1. Install the CLI once.
2. Run `automaton setup`.
3. Use `automaton new "My App"` for a new app, or `automaton link <app-id>` when the remote app already exists.
4. Run `automaton sync-ai` only if the generated local guidance files are missing or stale.
5. Deploy with `automaton deploy`.
6. Run `automaton validate` only when you need a separate diagnostic check.

## AI-readable files

These files remain the canonical source of truth and the fallback reference set when local generated guidance is unavailable:

- [llms.txt](https://automaton.monster/llms.txt)
- [llms/automaton-cli-install.md](https://automaton.monster/llms/automaton-cli-install.md)
- [llms/automaton-cli-deploy.md](https://automaton.monster/llms/automaton-cli-deploy.md)
- [llms/automaton-apps-overview.md](https://automaton.monster/llms/automaton-apps-overview.md)
- [llms/automaton-frontend-structure.md](https://automaton.monster/llms/automaton-frontend-structure.md)
- [llms/automaton-backend-controllers.md](https://automaton.monster/llms/automaton-backend-controllers.md)
- [llms/automaton-starter-prompts.md](https://automaton.monster/llms/automaton-starter-prompts.md)

## Official starter prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as a frontend-only Automaton app for: [describe the app]. Prefer plain HTML/CSS/JS with root-level files unless I ask for a framework. Use the Automaton CLI as the only execution path: start with automaton setup, create or link automaton.json with automaton new or automaton link, deploy with automaton deploy, and ask before publish. Only ask me to step in for authentication or publish approval.
```

## Prompt variants

Use the generic prompt above by default. Use these only when the situation is more specific.

### Existing app

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. I already have an Automaton app. Use the Automaton CLI as the only execution path: start with automaton setup, link this folder to app id [app-id] with automaton link, scaffold or update the app locally, deploy with automaton deploy, and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

### Frontend-only app

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as a frontend-only Automaton app for: [describe the app]. Prefer plain HTML, CSS, and JavaScript with root-level files unless I explicitly ask for a framework or split source structure. Do not default to `frontend/` for plain apps. Use the Automaton CLI as the only execution path: start with automaton setup, create or link automaton.json with automaton new or automaton link, deploy with automaton deploy, and ask before publish. Only ask me to step in for authentication or publish approval.
```

### React or Vue app

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as an Automaton app using [React or Vue]. Keep the source code in the repo. Use the Automaton CLI as the only execution path: start with automaton setup, create or link automaton.json with automaton new or automaton link, scaffold the app, make sure build and deployDir are correct, deploy with automaton deploy, and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

