---
title: Add a Runner Host
order: 30
---

# Add a Runner Host

## Goal

Register a server that can host managed runner containers.

## Server prerequisites

Prepare the target server with:

- a supported operating system;
- Docker installed and accessible to the Infrastructure Runner;
- required ports available;
- outbound connectivity to the Automaton API;
- sufficient CPU, memory, and disk;
- the required Docker network;
- the configured runtime directories;
- permissions for the Infrastructure Runner to execute the required host operations.

The exact provisioning transport and runtime connectivity mode determine which network and directory settings apply.

## Where

Admin → Runners → Runner Hosts

Host records can include a name, provider, region, public IP, private IP, provisioning transport, runtime connectivity mode, and capacity values. Use stable names such as `hetzner-fsn1-runner-01` or `runtime-local-docker`. Public Host Port connectivity requires a usable public IP; Local Docker Network is intended for local development.

Capacity includes total and reserved CPU, memory, and disk. Schedulable capacity is calculated as:

```text
Available capacity = Total - Reserved - Existing runner allocations
```

## Onboarding sequence

Administrators normally add remote Runner Hosts by installing and enrolling an Infrastructure Runner. The Infrastructure Runner reports host capabilities to Automaton and keeps the host record updated. Follow the detailed [Infrastructure Runner installation and maintenance](./infrastructure-runners.md) guide for the generated command and host-side procedure.

[![Runner Hosts via Infrastructure Runner](./assets/runner-hosts-via-infrastructure-runners.png)](./assets/runner-hosts-via-infrastructure-runners.png)

1. Open **Admin → Runners → Runner Hosts**.
2. Choose **Install Infrastructure Runner** for the target host and run the provided installer on the target server.
3. Wait for enrollment and confirm that the Infrastructure Runner appears online.
4. Validate the host. Validation is read-only: it reports missing prerequisites and does not silently repair the server. The installer and Infrastructure Runner setup perform the installation work; there is no separate **Prepare host** action in the current admin UI.
5. Approve the host when the administrator trusts it for managed workloads.
6. Approval automatically enables Hosted Runner scheduling.

## Ready conditions

A host is scheduling-eligible only when the Infrastructure Runner is online, validation succeeded, approval is complete when required, scheduling is enabled, the host is active, and enough capacity remains for the requested specification.
