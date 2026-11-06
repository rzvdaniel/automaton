---
title: Current limitations
description: What Automaton does not do yet, stated specifically.
---

# Current limitations

Automaton is early. These are the specific things to know before you rely on it.

## Platform

- **x86_64 Linux only.** Ubuntu and Debian 12 are supported (apt, systemd, Docker Engine) for the server and the Runner host. Other x86_64 Linux with systemd and Docker may work but is untested, and you install Docker yourself. ARM and fully offline or private-only installs are not supported yet.
- **Public DNS and HTTPS.** The server needs `api`, `studio`, `store`, `apps` and `*.apps` records and public ports 80, 443 and 5671.
- **A Runner is the agent you install on a server to run apps.** With one Runner, `automaton deploy` uses it automatically; with several you pick one per app. There is no automatic balancing.
- **No high availability.** The API, database and each Runner run on single machines. A machine that goes down takes its apps with it until you restore it.
- **No autoscaling or cross-region failover.** Each app runs where it is assigned.
- **No WebSockets or streamed responses.** Requests and responses are complete; apps poll for live updates.
- **Routing at large scale is unvalidated.** The proxy configuration holds per-app rules and is regenerated on change; behavior with thousands of apps has not been measured (see the [self-hosted core notes](https://github.com/rzvdaniel/automaton/blob/main/docs/roadmap/self-hosted-core.md)).

## Operations

- **Updates cause brief interruptions.** An update replaces the API, Worker, Studio and the app store; they can be unavailable for a short time.
- **Updates are not fully automatic.** Infrastructure changes and Runner-machine upgrades are separate steps you run.
- **Backups stay on the same server.** Nightly backups of the database and keys (not your apps' data files) are kept for 7 days on the server. Off-server copies and your hosting provider's snapshots are yours to arrange; a built-in off-site backup is planned ([#486](https://github.com/rzvdaniel/automaton/issues/486)).

## Security and teams

- **Built for people you trust.** Public multi-tenant hosting is out of scope. Isolation between users of one server is being formally accepted ([#521](https://github.com/rzvdaniel/automaton/issues/521)); until then do not host untrusted users.
- **No self sign-up.** Users are added by an administrator.
- **The Runner is trusted.** The Runner controls the machine's Docker, so treat Runner machines as part of your trusted base. A security overview is planned ([#562](https://github.com/rzvdaniel/automaton/issues/562)).

## Tooling

- **The CLI needs the .NET 10 SDK or later** to install from NuGet. The server installer does not need .NET.

We update this page as these are fixed. See the [roadmap](https://github.com/rzvdaniel/automaton/blob/main/docs/roadmap/README.md) for what is planned.
