---
title: Current limitations
description: What Automaton does not do yet, stated specifically.
---

# Current limitations

Automaton is early. These are the specific things to know before you rely on it.

## Platform

- **One kind of server.** x86_64 Ubuntu 24.04 or later. ARM, other distributions and offline installs are not the supported setup.
- **Public DNS and HTTPS.** The server needs `api`, `studio`, `store`, `apps` and `*.apps` records and public ports 80, 443 and 5671.
- **No high availability.** The API, database and each Runner run on single machines. A machine that goes down takes its apps with it until you restore it.
- **No autoscaling or cross-region failover.** Each app runs where you assign it.
- **No WebSockets or streamed responses.** Requests and responses are complete; apps poll for live updates.
- **Routing at large scale is unvalidated.** The proxy configuration holds per-app rules and is regenerated on change; behavior with thousands of apps has not been measured.

## Operations

- **Updates cause brief interruptions.** `update.sh` replaces the API, Worker, Studio and the app store; they can be unavailable for a short time.
- **Updates are not fully automatic.** Infrastructure changes and Runner-machine upgrades are separate steps you run.
- **Backups stay on the same server.** Nightly backups of the database and keys are kept for 7 days on the server. Off-server copies and your hosting provider's snapshots are yours to arrange; a built-in off-site backup is planned ([#486](https://github.com/rzvdaniel/automaton/issues/486)).
- **Restoring on a different, clean server is unverified.** The restore command exists and the steps are documented, but a restore onto a freshly installed second server has not been proven yet ([#524](https://github.com/rzvdaniel/automaton/issues/524)).

## Security and teams

- **Built for people you trust.** Public multi-tenant hosting is out of scope. Isolation between users of one server is being formally accepted ([#521](https://github.com/rzvdaniel/automaton/issues/521)); until then do not host untrusted users.
- **No self sign-up.** Users are added by an administrator.
- **The Runner is trusted.** The Runner controls the machine's Docker; apps do not get its socket or credentials, but you should treat Runner machines as part of your trusted base.

## Tooling

- **The CLI needs the .NET 9 SDK or later** to install from NuGet. The server installer does not need .NET.

We update this page as these are fixed. See the [roadmap](https://github.com/rzvdaniel/automaton/blob/main/docs/roadmap/README.md) for what is planned.
