---
title: FAQ
description: Short answers to common questions about Automaton.
---

# FAQ

## What is Automaton?
An open-source platform for deploying web apps, APIs and jobs on servers you control. See [Is Automaton for you?](./is-automaton-for-you).

## Does it cost anything?
The software is free under Apache 2.0. You pay for your servers, domain and any AI services you choose. There is no required Automaton subscription.

## How long does install take?
About 94 seconds for the installer on Debian 12, and 2-3 minutes from the first command to a working server on Ubuntu 24.04, including the administrator and a Runner. See [Install with your AI agent](./install-with-ai) and [the measurements](./numbers).

## Do I need an AI coding agent?
No, but Automaton is designed around one. The CLI generates an `AGENTS.md` and checks so a coding agent can build, validate, deploy and verify an app. You can also write the app yourself.

## What can I run?
Static sites, APIs and long-running servers in containers, and finite jobs called Actions. See [Build an Automaton app](./build-an-automaton-app).

## What can I not run?
Apps that need autoscaling or multi-region failover; see [Current limitations](./limitations).

## Can strangers sign up on my server?
No. Self sign-up is disabled. An administrator creates or invites each user.

## Where does my data live?
On your servers. Studio, the API, the database and every app run on machines you install them on.

## How do updates work?
Run `automaton server update --wait` or use Admin → Installation → Update in Studio. See [Update the server](./update-server).

## How are backups handled?
Nightly local backups of the database and keys are kept for 7 days. See [Back up and restore](./backup-restore).

## Can I use more than one server?
Yes. Install a Runner on each machine. With one Runner, `automaton deploy` uses it automatically; with several, you pick one per app (`automaton set-runner`). There is no automatic balancing or failover.

## Is it production-ready?
It runs on our own production server. Fresh-server installs, a restore onto a clean second server, and two-user isolation have been tested; see [Current limitations](./limitations) and [the measured runs](./numbers).

## How do I report a problem or ask a question?
Open an issue on [GitHub](https://github.com/rzvdaniel/automaton/issues).
