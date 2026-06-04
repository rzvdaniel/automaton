# Automaton Starter Prompts

Use these as starter prompts for Codex, Claude Code, Copilot Chat, Cursor, and similar AI tools.

Prefer the local generated Automaton guidance in the current folder after `automaton new` or `automaton link`. `automaton login` authenticates the CLI. `automaton new` creates `automaton.json` and local AI guidance. `automaton link` is only for existing apps. Use `https://automaton.monster/llms.txt` only as the fallback when local guidance is not available yet.

## Official generic starter prompt

```text
Build [describe the app] for Automaton Monster.

Use the Automaton CLI as the primary path. First run `automaton --version`. If the CLI is missing, install it using the official Automaton instructions. Then run `automaton login` and let me paste my Automaton API key if prompted.

For a new app, run `automaton new "[app-name]"` before creating app files. Then follow the local generated guidance in `AGENTS.md` and `.automaton/agents/`. If local guidance is unavailable, read https://automaton.monster/llms.txt.

Default to frontend-only Vite unless the requested feature truly requires server-side execution, secrets, persistence, auth, scheduled work, or browser-unsafe access. Put frontend source in `frontend/`, build to `frontend/dist`, use `base: './'`, and deploy with `automaton deploy`.

Do not publish publicly until I explicitly approve.
```

## Existing app link prompt

```text
Update my existing Automaton Monster app using the Automaton CLI.

First run `automaton --version`. If the CLI is missing, install it using the official Automaton instructions. Then run `automaton login` and let me paste my Automaton API key if prompted.

Link this folder to app id [app-id] with `automaton link [app-id]`. Then follow the local generated guidance in `AGENTS.md` and `.automaton/agents/`. If local guidance is unavailable, read https://automaton.monster/llms.txt.

Deploy with `automaton deploy`. Ask before publishing. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

## Frontend-only prompt

```text
Build [describe the app] as a frontend-only Automaton Monster app.

Use the Automaton CLI. Run `automaton --version`; if missing, install the official CLI. Run `automaton login` and let me paste my API key if prompted. Then run `automaton new "[app-name]"` before creating app files.

Follow the generated local guidance in `AGENTS.md` and `.automaton/agents/`. If it is missing, read https://automaton.monster/llms.txt.

Use Vite unless I ask for another framework. Put source in `frontend/`, build to `frontend/dist`, set Vite `base: './'`, deploy with `automaton deploy`, and ask before publishing.
```

## React prompt

```text
Build [describe the app] as a React app for Automaton Monster.

Use the Automaton CLI. Run `automaton --version`; if missing, install the official CLI. Run `automaton login` and let me paste my API key if prompted. Then run `automaton new "[app-name]"` before creating app files.

Follow the generated local guidance in `AGENTS.md` and `.automaton/agents/`. If it is missing, read https://automaton.monster/llms.txt.

Use Vite React under `frontend/`, build to `frontend/dist`, set Vite `base: './'`, deploy with `automaton deploy`, and ask before publishing. Create a backend only if the requested feature truly requires server-side execution, secrets, persistence, auth, scheduled work, or browser-unsafe access.
```

## Vue prompt

```text
Build [describe the app] as a Vue app for Automaton Monster.

Use the Automaton CLI. Run `automaton --version`; if missing, install the official CLI. Run `automaton login` and let me paste my API key if prompted. Then run `automaton new "[app-name]"` before creating app files.

Follow the generated local guidance in `AGENTS.md` and `.automaton/agents/`. If it is missing, read https://automaton.monster/llms.txt.

Use Vite Vue under `frontend/`, build to `frontend/dist`, set Vite `base: './'`, deploy with `automaton deploy`, and ask before publishing. Create a backend only if the requested feature truly requires server-side execution, secrets, persistence, auth, scheduled work, or browser-unsafe access.
```
