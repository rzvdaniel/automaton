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

1. Run `automaton setup` in the project or workspace.
2. Use `automaton new "My App"` for a new app, or `automaton link <app-id>` when the remote app already exists.
3. Keep the project in Git.
4. Point your AI tool to [llms.txt](https://automaton.monster/llms.txt) so it follows the required Automaton app structure.
5. Use your AI tool to scaffold and edit the app.
6. Run `automaton sync-ai` only if the generated local guidance files are missing or stale.
7. Deploy with `automaton deploy`.

## AI-readable files

- [llms.txt](https://automaton.monster/llms.txt)
- [llms/automaton-cli-deploy.md](https://automaton.monster/llms/automaton-cli-deploy.md)
- [llms/automaton-apps-overview.md](https://automaton.monster/llms/automaton-apps-overview.md)
- [llms/automaton-frontend-structure.md](https://automaton.monster/llms/automaton-frontend-structure.md)
- [llms/automaton-backend-controllers.md](https://automaton.monster/llms/automaton-backend-controllers.md)
- [llms/automaton-starter-prompts.md](https://automaton.monster/llms/automaton-starter-prompts.md)

## Starter prompt

```text
Read https://automaton.monster/llms.txt, then build this as an Automaton app for: [describe the app]. Use the Automaton CLI as the primary execution path. Start with automaton setup, create or link automaton.json in the project root with automaton new or automaton link, refresh local guidance with automaton sync-ai only if the generated files are missing or stale, run automaton validate, and for backend controllers pick an existing connected runner with automaton current-runner, automaton runners, and automaton set-runner <runner-id>, or tell the user to create or deploy one in Automaton Studio, then deploy. Only publish after explicit user confirmation.
```

## MCP recommendation

MCP is recommended when supported because it gives the AI more accurate Automaton-specific guidance. It is not required.