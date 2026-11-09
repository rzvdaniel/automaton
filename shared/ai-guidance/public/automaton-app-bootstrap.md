# Automaton

Automaton is a self-hosted platform for building, deploying and publishing web apps. The user runs their own server (Studio, API, app store) and one or more Runners that run the apps. Documentation: https://automaton.monster/docs/ . This file is the index for AI agents; every page below is one task with exact commands, a way to verify the result and common errors.

## Installing a server for the user

Before you run the installer, ask the user for what only they can give:

1. **A domain** whose DNS they control, with records pointing to the server as [Install the server](https://automaton.monster/docs/install-server) lists them (DNS only, no proxy).
2. **An email for certificate notices.** It is passed as `--email` and goes to Let's Encrypt, which issues the HTTPS certificates. Do not invent one.
3. **The first administrator, with a password you never see.** Deploying needs a signed-in CLI, so create the administrator yourself as [First administrator without a browser](https://automaton.monster/docs/first-admin#without-a-browser-ssh-or-an-ai-agent) describes, with a password generated into a shell variable that you never print, read back or write to a file. Install with `--quiet`, sign in the CLI, deploy the app, and end your last message with a one-time link for the user: run `sudo "$(command -v automaton)" server password-link --email <admin-email>` on the server and show its output. The link works once for 30 minutes; the user opens it, chooses their own password and the generated one stops working. If it expires, run the command again. Ask the user which email to use for the administrator.

## Starting an Automaton app

If you are in an empty directory and the user asks you to build an Automaton app:

1. Check whether the `automaton` CLI is available with `automaton --help`. If it is missing, follow [Create your first app](https://automaton.monster/docs/getting-started) (`dotnet tool install -g Automaton.Cli`).
2. Use the user's own Automaton API URL. If there is no server yet, follow [Install the server](https://automaton.monster/docs/install-server), [Create the first administrator](https://automaton.monster/docs/first-admin) and [Add a Runner](https://automaton.monster/docs/deploy-runner); for a team server, use its administrator-provided URL. Authenticate with `automaton login --server <api-url>` (add `--profile NAME` for several servers), then run `automaton new "My App" --template node|python|dotnet|static` (or no template) with a name appropriate to the request. Do not assume the public project website is the user's Automaton server.
3. Before implementing the app, read the generated `AGENTS.md`, `.automaton/agents/skills/automaton-app/SKILL.md` and `automaton.json`. They are the authoritative workflow for the project; this public guidance provides discovery and bootstrap.
4. Choose the language, framework and build tools after inspecting the user's requirements and the Runner environment. Do not assume that tools installed locally are available on the Runner.
5. Run `automaton validate`, `automaton deploy` and wait for `Succeeded`, verify the app's real behavior, then publish with `automaton publish --yes --slug NAME` when that is within the request ([Deploy and publish an app](https://automaton.monster/docs/deploy)).

The app's deployment target needs a Machine Management Runner. `automaton deploy` assigns the only online one; otherwise use `automaton set-runner` or `--runner`. For an existing remote app use `automaton link <app-id>` instead of creating another app.

Never paste or print API keys, tokens or passwords. Self sign-up is disabled; users come only from an administrator.
