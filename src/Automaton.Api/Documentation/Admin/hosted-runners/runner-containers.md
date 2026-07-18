---
title: Runner Containers
order: 50
---

# Runner Containers

Runner Containers are tenant runtime instances. The user’s effective subscription plan determines whether a container is allowed and which active specification is requested.

Provisioning selects a prepared, approved, scheduling-enabled host with enough capacity, allocates a runtime port when the host uses public host-port connectivity, and records the runtime endpoint.

The container keeps an **active specification snapshot** containing the contract used by the deployed runtime. During explicit reprovisioning, the newly requested contract is stored as a **pending specification snapshot**. The active snapshot, placement, port, and endpoint remain authoritative until provisioning succeeds.

Common lifecycle states include pending, provisioning, reprovisioning, running, failed, and disabled.

## Restart versus reprovision

**Restart** keeps the same container contract and placement and restarts the runtime.

**Reprovision** resolves the current plan specification, selects capacity again, and creates or replaces the container. It is the deliberate operation to apply a changed specification or move a runtime.

Provisioning can happen automatically through a user deployment flow or manually with **Provision Runner** in the admin view. Delete removes the managed runner and its lifecycle record; it does not create a replacement automatically.
