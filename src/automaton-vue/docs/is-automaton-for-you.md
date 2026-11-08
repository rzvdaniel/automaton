---
title: Is Automaton for you?
description: Who Automaton is for, who it is not for, and how it compares with a cloud provider.
---

# Is Automaton for you?

Automaton is a self-hosted platform for deploying web apps, APIs and scheduled jobs on servers you own or rent. You install it on an x86_64 Linux server, build apps with your own coding agent and the CLI, and manage them in your own Studio.

## Automaton fits you if

- You or your small team build many small apps, internal tools, sites or automations, and want one place to deploy and share them.
- You want your apps and data on machines you choose, at a cost set by the servers you rent.
- You are comfortable owning a Linux server: DNS, updates and backups are yours to run.
- You build with an AI coding agent and want deployment checks and agent instructions generated for you.

## Automaton does not fit you if

- You need automatic scaling, multi-region failover or a global edge network. Each app runs on one Runner machine (a Runner is the agent you install on a server to run apps); there is no autoscaling, no automatic balancing and no cross-region failover.
- You want a hosted service to sign up for. Automaton is software you install; there is no Automaton cloud and self sign-up is disabled on your server (an administrator adds users).
- You cannot give the server public DNS and HTTPS, or you need ARM or a fully offline or private-only installation. Those are not the supported setup.
- You need to host strangers' code. Automaton is for you and people you trust, not public multi-tenant hosting.

## Automaton or a cloud provider?

| | Automaton on your server | A cloud provider (PaaS or managed cloud) |
| --- | --- | --- |
| Control | You choose the machines, location and what runs where | The provider decides within its product |
| Data location | On your servers | In the provider's regions |
| Cost | Set by the server plans you choose, plus a domain; the software is free (Apache 2.0). Measured figures: see the table below | Depends on the provider's pricing model |
| Maintenance | You update the server and Runners and keep backups | The provider operates the platform |
| Availability | One server per Runner; if it is down, its apps are down | Redundancy and failover available |
| Scale | Add Runner machines yourself | Elastic scale on demand |
| Getting started | Install a server first, then deploy apps | No server to install first |
| On call | You are | The provider is, within its SLA |

Choose Automaton when control and data location matter more to you than managed scale and availability. Choose a cloud provider when you need to scale or cannot be the one on call.

### What it costs

For the more recent CX23 install, memory and update measurements, see [Automaton by the numbers](./numbers). The table below records an earlier run on a different server plan.

| Setup | Monthly cost | Install time |
| --- | --- | --- |
| Server and Runner on one Hetzner CPX22 (2 vCPU, 4 GB RAM, 80 GB disk, Helsinki) | €24.19 (server €23.58 + IPv4 €0.61, incl. 21% VAT), plus your domain | 90 seconds for the server installer; about 10 minutes from a new server to a first public app |

Measured on 3 November 2026 with release 0.2.0 ([#561](https://github.com/rzvdaniel/automaton/issues/561)). At idle the server and Runner used about 1.2 GB of memory with one small app. Larger apps, builds and more users need more; Hetzner bills by the hour, so you can resize later. Prices are Hetzner's list prices on that date and change over time.

## Next

- [Install your server](./install-server)
- [Current limitations](./limitations)
- [FAQ](./faq)
