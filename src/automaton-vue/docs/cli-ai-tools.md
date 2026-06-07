---
title: Build with AI Tools
description: Use Automaton CLI with AI coding tools like Copilot, Claude or Cursor to build and deploy apps faster.
---

# Build with AI

Automaton is designed to work with AI coding agents.

Use Cursor, Claude Code, Codex, GitHub Copilot Chat, VS Code, Gemini, or another coding agent and let it drive the Automaton CLI for you.

## Recommended AI-first workflow

1. Open an empty folder in your AI coding tool.
2. Paste the starter prompt below.
3. Let the AI install or verify the CLI.
4. Let the AI run `automaton login`.
5. Paste your API key into the terminal when prompted.
6. Let the AI create the app with `automaton new`.
7. Let the AI build and deploy the app.
8. Approve publishing when you are ready.

## Recommended starter prompt

```text
Build [APP IDEA] as an Automaton Monster app.

First fetch and read:
https://automaton.monster/llms.txt

Follow the instructions and references found there.
```

## Security note

Do not paste your API key directly into the AI chat.

Paste it into the terminal when `automaton login` asks for it.

## Existing app prompt

```text
Update my existing Automaton Monster app.

Read https://automaton.monster/llms.txt first.

Run `automaton login` if needed. Use `automaton link <app-id>` if this folder is not already linked.

Follow the generated local guidance in `AGENTS.md` and `.automaton/agents/`.

Deploy with `automaton deploy`.
```

## What the AI should usually do

- install or verify the Automaton CLI
- run `automaton login`
- create the app with `automaton new`
- follow local generated guidance
- generate or edit files locally
- deploy with `automaton deploy`
- publish only after approval

## What the AI should not do

- manually create `automaton.json` before trying `automaton new`
- invent an Automaton project structure
- create FastAPI, Flask, Express, or custom HTTP servers for Automaton backends
- publish without approval

## Next steps

- Read **Build Frontend App** for frontend-only apps.
- Read **Build Backend App** when server-side execution is required.
- Read **Deploy Runner** before publishing backend apps.
- Read **Deploy** and **Publish** for deployment details.
- Use the CLI reference only when you need advanced commands or flags.
