---
title: What is Automaton
description: What Automaton does, who it is for, what it costs and where its limits are, on one page.
order: 1
---

# What is Automaton

Automaton is the home for every app your AI builds, on a server you own. Your AI writes and deploys the app. Automaton runs it, gives it an address, decides who can open it, keeps its secrets and backs it up.

## What you get

- **Your own server.** Your apps and data stay on a machine you choose. The software is free (Apache 2.0); you pay only for the server and a domain.
- **Apps built by you or your AI.** Describe an app and your AI builds it with the CLI. See [Work with AI](./work-with-ai).
- **Studio.** The web page where you see every app: is it running, which version, who can open it, what went wrong.
- **The CLI.** `automaton` creates, deploys and checks apps, for you or your AI. See the [CLI reference](./cli).
- **A catalog.** Install a blog, analytics or link checker in one click, then ask your AI to adapt it. You can also publish your own catalog.
- **Sharing.** Every app is private until you choose: only you, people you choose, everyone on the server, or public.

## Is it for you?

Automaton fits you if:

- you or your small team build many small apps, internal tools, sites or automations;
- you want your apps and data on machines you choose, at a cost set by the server you rent;
- you are comfortable owning a Linux server: DNS, updates and backups are yours.

Choose a cloud provider instead if you need automatic scaling, multi-region failover, or someone else to be on call.

## What it costs and how long it takes

Measured on a Hetzner CX23 (2 vCPU, 4 GB RAM, about €6.64 per month at the time), these are observations, not guarantees:

- The server installer took 90 to 118 seconds; from the first command to a working server took 2-3 minutes.
- A Runner (the agent that runs apps on a server) installs in about 30 seconds.
- A server update took about 30 seconds.
- An idle server uses about 1.1 GB of memory and 4 GB of disk.

Details are in [issue #561](https://github.com/rzvdaniel/automaton/issues/561).

## Honest limits

- **x86_64 Linux only.** Ubuntu and Debian 12 are supported. ARM and fully offline installs are not.
- **Public DNS and HTTPS.** The server needs five DNS-only records (`api`, `studio`, `store`, `apps` and `*.apps` under your domain) and public ports 80, 443 and 5671.
- **No high availability.** The API, database and each Runner run on single machines. If one goes down, its apps are down until you restore it.
- **No autoscaling or cross-region failover.** Each app runs on one Runner; you add Runners yourself.
- **Updates cause brief interruptions** of the API, Studio and app store.
- **Backups need an off-site copy.** Nightly backups stay on the server for 7 days; install the [off-site backup app](./backup-restore) or arrange your own.
- **Built for people you trust.** It is not for hosting strangers' code. There is no self sign-up; an administrator adds users.
- **The Runner is trusted.** It controls Docker on its machine, so treat Runner machines as part of your trusted base. See the [security overview](./security).
- **There is no Automaton cloud.** It is software you install.

## Next

- [Work with AI](./work-with-ai) or [install the server yourself](./install)
- [Work with AI](./work-with-ai)
- [When something goes wrong](./when-something-goes-wrong)
