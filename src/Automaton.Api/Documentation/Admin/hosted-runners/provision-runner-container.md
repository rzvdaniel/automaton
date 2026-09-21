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

## Operate an existing Runner Container

In **Administration → Hosted Runners → Runner Containers**, select the existing container to inspect its current lifecycle and storage state.

- **Restart** requests a canonical restart and waits for the Runner to reconnect.
- **Verify storage** queues a host-side storage measurement only after the Hosted Runner is **Running** and its container is provisioned. While provisioning, the API rejects the request; refresh the container details after provisioning completes and then request verification.
- **Suspend** is available when the enforcement state is not already suspended or transitioning. Confirm the action before submitting it.
- **Resume** is available only when the enforcement state is **Suspended**.
- **Reprovision** replaces the managed container through the Hosted Runner lifecycle.
- **Delete hosted runner** stops the managed Runner and any Automaton app/build runtime containers that use its storage volumes, then lets you choose whether the persistent data is preserved or permanently deleted. The permanent-delete option requires typing the Runner name and cannot be undone. If a volume is still in use by a container that cannot be verified as owned by this Hosted Runner, deletion fails safely and can be retried through the canonical lifecycle after the conflict is resolved.

These controls require administrator access. The API validates the current lifecycle state and remains authoritative if the state changes between refreshes. While an action is pending, its control is disabled to prevent duplicate requests.
