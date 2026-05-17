# Automaton Starter Prompts

Use these as starter prompts for Codex, Claude Code, Copilot Chat, Cursor, and similar AI tools.

Prefer the local generated Automaton guidance in the current folder after `automaton setup`. Run `automaton setup` inside the app folder you want to create or manage. Each app should have its own `automaton.json` and local guidance scaffold. Use `https://automaton.monster/llms.txt` only as the fallback when local guidance is not available yet.

## Official generic starter prompt

```text
Build [describe the app] for Automaton Monster using the Automaton CLI. Start by running automaton setup, then follow the generated .automaton guidance in this folder. If local guidance is unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy and ask before publish. Only ask me to step in for authentication or publish approval.
```

## Existing app link prompt

```text
Update my existing Automaton Monster app using the Automaton CLI. Start by running automaton setup, link this folder to app id [app-id] with automaton link, then follow the generated .automaton guidance in this folder. If local guidance is unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

## Frontend-only prompt

```text
Build [describe the app] as a frontend-only Automaton Monster app using the Automaton CLI. Start by running automaton setup, then follow the generated .automaton guidance in this folder. If local guidance is unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy and ask before publish. Only ask me to step in for authentication or publish approval.
```

## React prompt

```text
Build [describe the app] as a React app for Automaton Monster using the Automaton CLI. Start by running automaton setup, then follow the generated .automaton guidance in this folder. If local guidance is unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

## Vue prompt

```text
Build [describe the app] as a Vue app for Automaton Monster using the Automaton CLI. Start by running automaton setup, then follow the generated .automaton guidance in this folder. If local guidance is unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```
