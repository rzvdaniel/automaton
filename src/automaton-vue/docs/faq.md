---
title: FAQ
description: Short answers to common questions about Automaton.
---

# FAQ

## What is Automaton?
An open-source platform for deploying web apps, APIs and scheduled jobs on servers you control. See [Is Automaton for you?](./is-automaton-for-you).

## Does it cost anything?
The software is free under Apache 2.0. You pay for your servers, domain and any AI services you choose. There is no required Automaton subscription.

## Do I need an AI coding agent?
No, but Automaton is designed around one. The CLI generates an `AGENTS.md` and checks so a coding agent can build, validate, deploy and verify an app. You can also write the app yourself.

## What can I run?
Static sites, APIs and long-running servers in containers, and finite jobs called Actions. See [Build an Automaton App](./build-an-automaton-app).

## What can I not run?
Apps that need WebSockets or streamed responses; see [Current limitations](./limitations).

## Can strangers sign up on my server?
No. Self sign-up is disabled. An administrator creates or invites each user.

## Where does my data live?
On your servers. Studio, the API, the database and every app run on machines you install them on.

## How do updates work?
Run `sudo bash /opt/automaton-server/update.sh`. It verifies the release, makes a local backup and replaces the services, with a brief interruption. See [Install your server](./install-server#update).

## How are backups handled?
Nightly local backups of the database and keys are kept for 7 days. Also use your hosting provider's snapshots and copy backups elsewhere. See [Install your server](./install-server#backups).

## Can I use more than one server?
Yes. Install a Runner on each machine and choose where each app runs. There is no automatic placement or failover.

## Is it production-ready?
It runs on our own production server. Clean-server install timing, restore on a clean server and two-user isolation are still being proven; see [Current limitations](./limitations).

## How do I report a problem or ask a question?
Open an issue on [GitHub](https://github.com/rzvdaniel/automaton/issues).
