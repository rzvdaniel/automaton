---
title: Work with AI
description: You describe the app and judge the result. Your AI builds, deploys and fixes it. Automaton runs it, shares it and keeps it safe.
order: 5
---

# Work with AI

With Automaton you care about the app you are making, not about servers, hosting or publishing. You say what you want and judge the result. Your AI does the hard work. Automaton runs the app, shares it and keeps it safe.

## The loop

1. **Describe it.** "An app that checks my website every 5 minutes and emails me when it is down."
2. **Your AI builds and deploys it.** It writes the code, runs it on your server and checks that it works before it tells you.
3. **You look at it in Studio.** Open the app, try it, read what it did.
4. **You say what to change.** "Make the email shorter." "Add a page with the last 24 hours."
5. **Repeat until you are happy.**

You never write a Dockerfile, open a port, buy a certificate or set up a domain for an app.

## Set up once

You need an Automaton server (your AI can [install it](./install-with-ai)) and an AI that can run commands on your computer, such as [Claude Code](https://claude.com/claude-code).

1. Install the CLI. Linux or macOS:

   ```bash
   curl -fsSL https://github.com/rzvdaniel/automaton/releases/latest/download/install.sh | sh
   ```

   Windows (PowerShell): `irm https://github.com/rzvdaniel/automaton/releases/latest/download/install.ps1 | iex`

2. Sign in to your server, once per computer: `automaton login --server https://api.yourdomain.com`
3. Open an empty folder in your AI and start.

## Your first prompt

> Build me an Automaton app that checks https://example.com every 5 minutes and emails me when it is down. Use the Automaton CLI, deploy it, check that it runs, then tell me where to see it in Studio.

Your AI creates the app with `automaton new`. That also writes `AGENTS.md`: the instructions that teach your AI how to build, check and deploy an Automaton app, so you never explain it again.

## Check it in Studio

Open Studio, then **Apps** and your app:

- **Overview:** is it running, which version, how much disk it uses, and anything that needs you.
- **Activity:** every run and deploy, and what went wrong.
- **Open app:** the app itself, if it has pages.

Something not right? Tell your AI what you see, in your own words. It reads the logs, fixes the code and deploys again.

## Start from what exists

Before asking for something big, look in **Catalog**. A blog, analytics, link checker or log viewer may already be there, tested and one click away. Install it, then ask your AI to adapt it: "Fork Goat Counter and add a weekly email with the top pages."

## Share it when it is ready

Every app starts private. With **Share** (or by asking your AI) you choose:

| Who can open it | For |
|---|---|
| Only you | trying things |
| Signed-in members | your team or family |
| Everyone with the link | anything public |

To show it off, publish it in your own app store with a cover and a description.

## Day two

Studio **Home** tells you what needs you: an update, a failed run, a permission to approve. Hand it to your AI: "The checker failed at 17:13, look at the logs and fix it." When a new Automaton version comes out, update from **Server** in one click; apps keep running.

## Prompts that work well

- "Deploy it and check it works before you tell me it's done."
- "It failed at 17:13. Read the logs and fix it."
- "Add a setting for the address to check, so I can change it in Studio."
- "Keep the API key as a secret, not in the code."
- "Let my team open it." / "Publish it in the store."

## Good to know

- **Your apps, your server.** Apps and their data stay on machines you choose. The cost is the server you rent.
- **Secrets stay out of the chat.** You type keys and passwords in Studio or the CLI; your AI only names them.
- **You approve what apps may do.** Sending email or reading the server needs an administrator's approval in Studio.
- **Data survives new versions.** What an app keeps in its data folder stays across every deploy.
- **A bad version does not go live.** A new version replaces the old one only after its health check passes.

Next: [Build an app](./build-an-automaton-app) for what your AI writes, and the [CLI](./automaton-cli) for every command.
