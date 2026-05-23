# 5-Minute Quickstart

If you want the shortest path from zero to a deployed app, use this flow:

```bash
dotnet tool install -g Automaton.Cli
automaton setup
automaton new "My App"
```

Then open your AI tool in that folder and paste:

```text
Build a task management app for Automaton Monster.
Use React frontend and Python backend.
Deploy with automaton deploy.
Ask me before publish.
```

When the app is ready:

```bash
automaton deploy
automaton publish
```

## What this gives you

- A local project you can edit in Cursor, Claude Code, VS Code, or another IDE
- A deployed app on Automaton
- A public URL when you choose to publish

## Default command model

For most users, these are the only commands that matter:

```bash
automaton setup
automaton new
automaton deploy
automaton publish
```

Everything else is advanced.

## What to read next

- **Build with AI** if you want copy-paste prompts and the recommended workflow
- **Choose a Stack** if you need help picking plain HTML, React, or Vue
- **Deploy** if you want the simplest explanation of what deploy does
