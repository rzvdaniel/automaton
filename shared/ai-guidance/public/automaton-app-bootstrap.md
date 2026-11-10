# Automaton

Automaton is a self-hosted platform for building, deploying and publishing web apps. The user runs their own server (Studio, API, app store) and one or more Runners that run the apps. Documentation: https://automaton.monster/docs/ . This file is the index for AI agents; every page below is one task with exact commands and a check after each step.

## Start here

Every page below is one task with exact commands and a check after each step. Follow them in order and do not skip the checks.

**Installing a server for the user.** Pages, in order: [Install the server](https://automaton.monster/docs/install-server) (section "For AI agents"), [Create the first administrator](https://automaton.monster/docs/first-admin), [Install and authenticate the CLI](https://automaton.monster/docs/automaton-cli), [Add a Runner](https://automaton.monster/docs/deploy-runner). Overview for the user: [Install with your AI agent](https://automaton.monster/docs/install-with-ai).

- Ask the user for the server's SSH access, the domain and the certificate email (also which email the administrator uses).
- Never print, read back or store the generated administrator password.
- Finish with the one-time link from `server password-link` and show it in your last message.

**Starting an Automaton app.** Pages, in order: [Build an Automaton app](https://automaton.monster/docs/build-an-automaton-app), [Deploy and publish an app](https://automaton.monster/docs/deploy). Overview for the user: [Your first app](https://automaton.monster/docs/getting-started).

- Wait until the app is `Running` before reporting; `deploy` has no `--yes` flag.
- Publish only if the user asked.
- A team server's API URL comes from its administrator; do not assume the public project website is the user's server.

## Working in an app folder

Before implementing, read the generated `AGENTS.md`, `.automaton/agents/skills/automaton-app/SKILL.md` and `automaton.json`. They are the authoritative workflow for the project. Choose the language and tools from the user's requirements and the Runner environment; do not assume local tools exist on the Runner. For an existing remote app use `automaton link <app-id>` instead of creating another app. More options: [Deploy and publish an app](https://automaton.monster/docs/deploy).

Never paste or print API keys, tokens or passwords. Self sign-up is disabled; users come only from an administrator.
