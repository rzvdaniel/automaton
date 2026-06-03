# Build with AI

This is the recommended local workflow for most developers.

Use Automaton with the AI coding tool you already like: Cursor, Claude Code, GitHub Copilot, Codex, VS Code, or another local assistant.

## One-time setup

Install the CLI:

```bash
dotnet tool install -g Automaton.Cli
```

Prepare the current folder:

```bash
automaton setup
```

Create a new app, or link an existing one:

```bash
automaton new "My App"
# or
automaton link <app-id>
```

## Recommended AI-first flow

1. Open the app folder in your AI coding tool.
2. Ask the AI to build the app locally.
3. Tell it to deploy with `automaton deploy`.
4. Tell it to ask before publishing.

## Best starter prompt

```text
Build a task management app for Automaton Monster.
Use React frontend and Python backend.
Deploy with automaton deploy.
Ask me before publish.
```

## More examples

### Frontend-only

```text
Build a habit tracker app for Automaton Monster.
Use a frontend-only app.
Deploy with automaton deploy.
Ask me before publish.
```

### Vue

```text
Build a poker planning app for Automaton Monster.
Use Vue frontend and Python backend.
Deploy with automaton deploy.
Ask me before publish.
```

### Existing app

```text
Update my existing Automaton Monster app in this folder.
Use automaton link <app-id> if needed.
Deploy with automaton deploy.
Ask me before publish.
```

## What the AI usually needs to do

- generate or edit files locally
- keep `automaton.json` correct
- run `automaton deploy`
- run `automaton publish` only after approval

That is the happy path.

## Next steps

Read the frontend app guide if you are building a static app, Vue app, React app, or browser-only tool.

Read the backend app guide if your app needs Python APIs, server-side logic, integrations, or private credentials.

Read the runner guide before publishing a backend app.

Read the deploy and publish guides when you want the shortest explanation of shipping an app live.

Use the CLI reference only when you need advanced commands or flags.
