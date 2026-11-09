# Automaton

Automaton is a self-hosted platform for building, deploying and publishing web apps. The user runs their own server (Studio, API, app store) and one or more Runners that run the apps. Documentation: https://automaton.monster/docs/ . This file is the index for AI agents; every page below is one task with exact commands and a check after each step.

## Start here

1. **No server yet:** follow [Install with your AI agent](https://automaton.monster/docs/install-with-ai). Ask the user only for what they alone can give: root SSH access to an x86_64 Ubuntu 24.04/22.04 or Debian 12 server, a domain with two DNS A records (`domain` and `*.domain`, DNS only, no proxy), and an email for Let's Encrypt notices (also ask which email the administrator uses; default: the same). The page installs the server, creates the administrator with a password you never see, signs in the CLI, adds a Runner and ends with a one-time link the user opens to choose their own password. Show that link in your last message.
2. **Server and signed-in CLI exist:** follow [Your first app](https://automaton.monster/docs/getting-started): `automaton new`, edit, `automaton validate`, `automaton deploy`, wait for `Running` in `automaton apps`, `automaton publish --yes --slug NAME`. A team server's API URL comes from its administrator; do not assume the public project website is the user's server.

## Working in an app folder

Before implementing, read the generated `AGENTS.md`, `.automaton/agents/skills/automaton-app/SKILL.md` and `automaton.json`. They are the authoritative workflow for the project. Choose the language and tools from the user's requirements and the Runner environment; do not assume local tools exist on the Runner. For an existing remote app use `automaton link <app-id>` instead of creating another app. More options: [Deploy and publish an app](https://automaton.monster/docs/deploy).

Never paste or print API keys, tokens or passwords. Self sign-up is disabled; users come only from an administrator.
