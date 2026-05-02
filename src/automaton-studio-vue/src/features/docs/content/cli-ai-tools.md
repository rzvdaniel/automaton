# Workflow: External IDE + CLI + AI tools

Use this path when you want to stay in Cursor, Claude Code, Copilot Chat, or another tool while keeping the project local.

## Recommended flow

1. Create or link the app.
2. Keep the project in Git.
3. Use your AI tool to scaffold and edit the app.
4. Deploy with `automaton deploy`.

## Starter prompt

```text
Build this as an Automaton app. Use any static frontend stack that fits the task. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Use backend/shared/ for shared helpers, backend/requirements.txt for extra Python packages, backend/mocks/{controller}.{action}.json for local mock responses, keep automaton.json in the project root, and deploy with automaton deploy.
```

## MCP recommendation

MCP is recommended when supported because it gives the AI more accurate Automaton-specific guidance. It is not required.