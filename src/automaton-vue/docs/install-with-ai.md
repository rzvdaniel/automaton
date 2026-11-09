---
title: Install with your AI agent
description: Give your AI agent a server, a domain and an email. It installs Automaton and hands you a one-time link to choose your password.
---

# Install with your AI agent

Your AI agent can take an empty server to a working Automaton in a few minutes. You do not run any commands.

## What you provide

1. **A server you can reach with SSH.** A Linux server (Ubuntu 22.04 or 24.04, or Debian 12) with at least 2 GB of memory.
2. **A domain with two DNS records** pointing to the server: `yourdomain.com` and `*.yourdomain.com`, DNS only (no proxy).
3. **An email address** for certificate notices. It is not an account. Your AI will also ask which email you want for your administrator account.

## What your AI does

- Installs Automaton on your server.
- Creates your administrator account. It never sees your password: it uses a temporary one that stops working when you choose yours.
- Adds a Runner, the part of Automaton that runs your apps.
- If you ask, builds and deploys a first app ([Your first app](./getting-started)).

## What you get

A working Automaton with Studio at `https://studio.yourdomain.com`, and a one-time link in your AI's last message.

1. Open the link and choose your password.
2. Sign in to Studio with your administrator email and the new password.

The link works once and for 30 minutes. If it expired, ask your AI for a new one.

## Try it

Give your AI something like:

> Install Automaton on my server `203.0.113.10` (SSH as root). My domain is `yourdomain.com`, certificate email `me@yourdomain.com`. Follow https://automaton.monster/llms.txt, then give me the link to choose my password.

## Technical details

For people who want to run or check the steps, and for agents:

- [Install the server](./install-server) (agent steps included)
- [Create the first administrator](./first-admin)
- [Add a Runner](./deploy-runner)
- [llms.txt](https://automaton.monster/llms.txt) for agents
- [Troubleshooting](./troubleshooting)
