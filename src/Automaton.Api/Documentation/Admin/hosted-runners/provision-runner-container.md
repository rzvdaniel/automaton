---
title: Provision a Runner Container
order: 40
---

# Provision a User’s Runner Container

## Automatic flow

1. The user has an effective Subscription Plan.
2. The plan has Hosted Runner enabled.
3. The plan references an active specification.
4. Global provisioning is enabled.
5. The scheduler finds an eligible host with enough capacity.
6. A HostedRunner record is created.
7. The specification snapshot is stored.
8. A runtime port is allocated when the host uses public host-port connectivity.
9. A provisioning request is sent.
10. The container starts.
11. The Runner connects to Automaton.
12. User applications may be assigned to it.

## Manual admin flow

Administration → Hosted Runners → Runner Containers → **Provision Runner**

The current dialog selects a user. The specification is resolved from that user’s effective subscription plan, and the host is selected automatically from eligible capacity. The current UI does not expose specification, host, or force-provision overrides in the dialog.

## Verify after provisioning

Confirm:

- Status is **Running**;
- Runner connectivity is online;
- a host is assigned;
- a runtime port is assigned when applicable;
- a container ID is present;
- the active specification snapshot is the expected one;
- no pending specification snapshot remains.

If the container is provisioned but the Runner does not connect, inspect the deployment status, container logs, Runner registration, and API connectivity before retrying.
