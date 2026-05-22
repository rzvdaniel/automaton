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

## Current contract

Keep the app model simple:

- Local authoring usually uses `frontend/` and `backend/`.
- Server-side editable files live in `source/frontend/` and `source/backend/`.
- Deploy writes runtime files to `live/frontend/` and `live/backend/`.
- Public serving reads only `live/frontend/`.
- Runner execution reads only `live/backend/`.

### AI-first flow

1. Install the CLI once.
2. Run `automaton setup` in the app folder you want to create or manage.
3. Open your preferred AI tool in the app folder.
4. Ask it to build and deploy the app to Automaton Studio.

Each app should have its own `automaton.json` and local guidance scaffold. After `automaton setup`, the AI should be able to handle most of the remaining work in the local project: scaffold or edit files, create or link the app, keep `automaton.json` correct, run deploy preflight, handle runner selection for backend apps, deploy, and ask before publish. The user should usually only need to step in for interactive auth, runner creation outside the CLI, or publish approval.

### Manual fallback

1. Install the CLI once.
2. Run `automaton setup`.
3. Use `automaton new "My App"` for a new app, or `automaton link <app-id>` when the remote app already exists.
4. Run `automaton sync-ai` only if the generated local guidance files are missing or stale.
5. Deploy with `automaton deploy`.
6. Publish with `automaton publish` only when you want the app publicly accessible.
7. Run `automaton validate` only when you need a separate diagnostic check.

If a backend app has no runner yet, `automaton deploy` may still succeed with warnings after uploading frontend and backend runtime. In that case the AI should explain that backend was uploaded but is not running, then suggest `automaton deploy --runner <runner-id-or-name>` or creating/deploying a runner in Automaton Studio.

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
Build [describe the app] for Automaton Monster using the Automaton CLI. Start by running automaton setup, then follow the generated .automaton guidance in this folder. If local guidance is unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy. If deploy succeeds, ask whether to publish; if I approve, run automaton publish and return the public URL. Only ask me to step in for authentication or publish approval.
```

## Prompt variants

Use the generic prompt above by default. Use these only when the situation is more specific.

### Existing app

```text
Update my existing Automaton Monster app using the Automaton CLI. Start by running automaton setup, link this folder to app id [app-id] with automaton link, then follow the generated .automaton guidance in this folder. If local guidance is unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy. If deploy succeeds, ask whether to publish; if I approve, run automaton publish and return the public URL. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

### Frontend-only app

```text
Build [describe the app] as a frontend-only Automaton Monster app using the Automaton CLI. Start by running automaton setup, then follow the generated .automaton guidance in this folder. If local guidance is unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy. If deploy succeeds, ask whether to publish; if I approve, run automaton publish and return the public URL. Only ask me to step in for authentication or publish approval.
```

### React or Vue app

```text
Build [describe the app] as a [React or Vue] app for Automaton Monster using the Automaton CLI. Start by running automaton setup, then follow the generated .automaton guidance in this folder. If local guidance is unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy. If deploy succeeds, ask whether to publish; if I approve, run automaton publish and return the public URL. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

