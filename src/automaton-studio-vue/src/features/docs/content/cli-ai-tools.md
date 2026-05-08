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

### 2. Prepare the current machine or workspace

```bash
automaton setup
```

Get an API key from **Settings → API Keys → New Key** (scope: `execute`).

For the full CLI reference — all commands, options, `automaton.json` fields, and CI/CD usage — see the [Automaton CLI](/docs/automaton-cli) page.

---

## Recommended flow

### AI-first flow

1. Install the CLI once.
2. Run `automaton setup` in the project or workspace.
3. Open your preferred AI tool in the app folder.
4. Ask it to build and deploy the app to Automaton Studio.

After `automaton setup`, the AI should be able to handle most of the remaining work in the local project: scaffold or edit files, create or link the app, keep `automaton.json` correct, run validation, guide runner assignment for backend apps, and deploy. The user should usually only need to step in for interactive auth, runner creation outside the CLI, or publish approval.

### Manual fallback

1. Install the CLI once.
2. Run `automaton setup`.
3. Use `automaton new "My App"` for a new app, or `automaton link <app-id>` when the remote app already exists.
4. Run `automaton sync-ai` only if the generated local guidance files are missing or stale.
5. Run `automaton validate`.
6. If `backend/*.py` exists, assign a connected runner.
7. Deploy with `automaton deploy`.

## AI-readable files

These files remain the canonical source of truth and the fallback reference set when local generated guidance is unavailable:

- [llms.txt](https://automaton.monster/llms.txt)
- [llms/automaton-cli-deploy.md](https://automaton.monster/llms/automaton-cli-deploy.md)
- [llms/automaton-apps-overview.md](https://automaton.monster/llms/automaton-apps-overview.md)
- [llms/automaton-frontend-structure.md](https://automaton.monster/llms/automaton-frontend-structure.md)
- [llms/automaton-backend-controllers.md](https://automaton.monster/llms/automaton-backend-controllers.md)
- [llms/automaton-starter-prompts.md](https://automaton.monster/llms/automaton-starter-prompts.md)

## Starter prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Then build this as an Automaton app for: [describe the app]. Use the Automaton CLI as the primary execution path. Start with automaton setup. After that, handle the remaining workflow yourself: create or link automaton.json in the project root, refresh local guidance with automaton sync-ai only if the generated files are missing or stale, run automaton validate, and for backend controllers pick an existing connected runner with automaton current-runner, automaton runners, and automaton set-runner <runner-id>, or tell the user to create or deploy one in Automaton Studio, then deploy. Only publish after explicit user confirmation.
```

## MCP recommendation

MCP is recommended when supported because it gives the AI more accurate Automaton-specific guidance. It is not required.