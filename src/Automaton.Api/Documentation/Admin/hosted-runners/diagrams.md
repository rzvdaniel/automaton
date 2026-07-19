---
title: Diagrams
order: 90
---

# Hosted Runner Diagrams

Use these diagrams as a visual reference for the Hosted Runner architecture and setup process. The same diagrams also appear in the relevant task guides.

## Hosted Runner domain model

This diagram explains how subscription plans, hosted runner specifications, runner hosts, and hosted runners relate to each other.

[![Hosted Runner domain model](./assets/automaton-runtime-plans-runner-hosts-specifications.png)](./assets/automaton-runtime-plans-runner-hosts-specifications.png)

Related guides:

- **Initial Setup** — verify the platform configuration and setup readiness.
- **Create a Specification** — define the runtime size and capabilities.
- **Configure a Subscription Plan** — map plans to hosted runner specifications.
- **Provision a Runner Container** — provision a hosted runner for an eligible user.

## Runner Hosts and Infrastructure Runners

This diagram explains how a server becomes a Runner Host through Infrastructure Runner installation and enrollment.

[![Runner Hosts through Infrastructure Runners](./assets/runner-hosts-via-infrastructure-runners.png)](./assets/runner-hosts-via-infrastructure-runners.png)

Related guides:

- **Add a Runner Host** — install and enroll an Infrastructure Runner.
- **Initial Setup** — confirm that an eligible host is available.
- **Troubleshooting** — investigate enrollment, validation, and host readiness problems.
