# Automaton Starter Prompts

Use these as starter prompts for Codex, Claude Code, Copilot Chat, Cursor, and similar AI tools.

Prefer the local generated Automaton guidance in the current folder after `automaton new` or `automaton link`. `automaton login` authenticates the machine globally through browser/device authorization. `automaton login --local` is only for project-specific credential isolation. `automaton new` creates `automaton.json` and local AI guidance. `automaton link` is only for existing apps. Use `https://automaton.monster/llms.txt` only as the fallback when local guidance is not available yet.

## Official generic starter prompt

```text
Build [APP IDEA] as an Automaton Monster app.

First fetch and read:
https://automaton.monster/llms.txt

Follow the instructions and references found there.
```

## Existing app link prompt

```text
Update my existing Automaton Monster app using the Automaton CLI.

First run `automaton --version`. If the CLI is missing, install it using the official Automaton instructions. Then run `automaton login` to authorize this machine through Automaton Studio. Do not ask the user to paste an API key for normal onboarding. For CI/CD, use the `AUTOMATON_API_KEY` environment variable directly with the required CLI command.

Link this folder to app id [app-id] with `automaton link [app-id]`. Then follow the local generated guidance in `AGENTS.md` and `.automaton/agents/`. If local guidance is unavailable, read https://automaton.monster/llms.txt.

Deploy with `automaton deploy`. Ask before publishing. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

## Optional technology template prompt

```text
Build [describe the app] using [technology] as an optional implementation choice.

Use the Automaton CLI. Run `automaton --version`; if missing, install the official CLI. Authenticate with `automaton login`, then run `automaton new "[app-name]"` before creating app files.

Follow the generated local guidance in `AGENTS.md` and `.automaton/agents/`. If it is missing, read https://automaton.monster/llms.txt.

Use the selected technology's normal source layout, but define the generic
application contract explicitly: structured build command and output, runtime
executable and arguments, working directories, port, optional health path,
environment, and persistent data directory. Deploy with `automaton deploy`.
The technology choice is a template convenience, not an Automaton requirement.
```
