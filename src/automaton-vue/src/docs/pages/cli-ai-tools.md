# Build with AI

This is the recommended local workflow for most developers.

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

1. Open the app folder in Cursor, Claude Code, Copilot Chat, or another AI tool.
2. Ask the AI to build the app.
3. Tell it to deploy with `automaton deploy`.
4. Tell it to ask before publish.

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

## When to read more

- Read **Choose a Stack** if you need help picking plain HTML, React, or Vue
- Read **Codex Browser Debugging** if you want Codex to open and test your localhost app
- Read **Deploy** if you want the simplest deploy explanation
- Read **CLI Reference** only when you need advanced commands
