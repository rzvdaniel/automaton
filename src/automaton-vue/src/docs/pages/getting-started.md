# 5-Minute Quickstart

If you want the shortest path from zero to a deployed app, choose one of these flows.

## CLI path

```bash
dotnet tool install -g Automaton.Cli
automaton login
automaton new "My App"
automaton deploy
```

Use `automaton publish` only when you are ready to make the app public.

## AI-agent path

You can also let an AI coding agent create and deploy the app for you.

Paste this into Cursor, Claude Code, Codex, GitHub Copilot Chat, VS Code, or another coding agent:

```text
Build [APP IDEA] as an Automaton Monster app.

First fetch and read:
https://automaton.monster/llms.txt

Follow the instructions and references found there.
```

Do not paste your API key directly into the AI chat. Paste it into the terminal when `automaton login` asks for it.

## What this gives you

- A local project you can edit in Cursor, Claude Code, VS Code, or another IDE
- A deployed app on Automaton
- A public URL when you choose to publish

## Default command model

For most users, these are the only commands that matter:

```bash
automaton login
automaton new
automaton deploy
automaton publish
```

Everything else is advanced.

## What to read next

- **Build with AI** if you want more copy-paste prompts and the recommended workflow
- **Build Frontend App** if you are building a static app, React app, Vue app, or browser-only tool
- **Build Backend App** if your app needs server-side logic, integrations, or private credentials
- **Deploy** if you want the simplest explanation of what deploy does
