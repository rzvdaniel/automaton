---
title: Overview
order: 0
---

# Hosted Runners Overview

Hosted Runners combine commercial entitlement, infrastructure capacity, and managed runtime lifecycle:

```text
Subscription Plan
        |
        v
Hosted Runner capability
        |
        v
Hosted Runner Specification
        |
        v
Runner Container
        |
        v
Runner Host
```

The objects have distinct responsibilities:

- **Runner Host** — a prepared server that provides capacity and is eligible for scheduling.
- **Runner Container** — a managed runtime instance assigned to a user.
- **Specification** — the technical resource contract used when provisioning a container.
- **Subscription Plan** — the commercial product that grants or withholds the hosted-runner capability and selects a specification.

The global **Allow hosted runner provisioning** setting is a platform-wide kill switch. When it is disabled, new provisioning and explicit reprovisioning are rejected; it does not resize or suspend already-running containers.
