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
- **Open streams and WebSockets are capped per app.** Server-Sent Events and WebSockets work, but one app holds at most 20 at a time and an idle one closes after five minutes.
- **Routing at large scale is unvalidated.** The proxy configuration holds per-app rules and is regenerated on change; behavior with thousands of apps has not been measured (see the [self-hosted core notes](https://github.com/rzvdaniel/automaton/blob/main/docs/roadmap/self-hosted-core.md)).

## Operations

- **Updates cause brief interruptions.** An update replaces the API, Worker, Studio and the app store; they can be unavailable for a short time.
- **Updates are not fully automatic.** Infrastructure changes and Runner-machine upgrades are separate steps you run.
- **Backups need an off-site copy.** Nightly backups of the database and keys (not your apps' data files) are kept for 7 days on the server. An [off-site backup app](./backup-restore) is available from the catalog, but you must install and configure it or arrange your own off-site copies.

## Security and teams

- **Built for people you trust.** Public multi-tenant hosting is out of scope. Two-user isolation and restart checks passed on the test server ([#521](https://github.com/rzvdaniel/automaton/issues/521)); that does not make the platform suitable for hosting strangers' code.
- **No self sign-up.** Users are added by an administrator.
- **The Runner is trusted.** The Runner controls the machine's Docker, so treat Runner machines as part of your trusted base. See the [security overview](./security).

## Tooling

- **The CLI needs the .NET 10 SDK or later** to install from NuGet. The server installer does not need .NET.

We update this page as these are fixed. See the [roadmap](https://github.com/rzvdaniel/automaton/blob/main/docs/roadmap/README.md) for what is planned.
