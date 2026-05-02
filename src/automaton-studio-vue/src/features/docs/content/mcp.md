# Workflow: MCP Server

Use this path when your AI tool supports MCP and you want the most accurate Automaton-specific guidance.

## What MCP helps with

- App linking and discovery
- Reading existing files
- Teaching the AI the Automaton structure

## What MCP does not replace

- Your local project
- Your Git workflow
- `automaton deploy`

If you are not using MCP in a given tool or session, point that AI tool to [llms.txt](https://automaton.monster/llms.txt) instead.

## AI-readable files

- [llms.txt](https://automaton.monster/llms.txt)
- [llms/automaton-apps-overview.md](https://automaton.monster/llms/automaton-apps-overview.md)
- [llms/automaton-frontend-structure.md](https://automaton.monster/llms/automaton-frontend-structure.md)
- [llms/automaton-backend-controllers.md](https://automaton.monster/llms/automaton-backend-controllers.md)
- [llms/automaton-starter-prompts.md](https://automaton.monster/llms/automaton-starter-prompts.md)

## VS Code example

```json
{
  "servers": {
    "automaton": {
      "type": "http",
      "url": "https://api.automaton.monster/api/mcp",
      "headers": {
        "X-API-Key": "your-api-key"
      }
    }
  }
}
```

## Claude Code example

```bash
claude mcp add automaton --transport http "https://api.automaton.monster/api/mcp" --header "X-API-Key: your-api-key"
```