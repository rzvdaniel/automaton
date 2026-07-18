---
title: Create a Specification
order: 10
---

# Create a Runner Specification

## Where

Administration → Hosted Runners → Specifications → **New specification**

## Fields

- **Name** — human-readable administrator label, such as `Free Hosted Runner`, `Pro Hosted Runner`, or `Business Hosted Runner`.
- **Code** — stable machine identifier, such as `free`, `pro`, or `business`. Use lowercase letters and numbers without spaces. Keep the code stable after plans start using it; display names may change.
- **CPU limit** — CPU capacity assigned to the container, not the whole host. Typical examples are 0.25 for Free, 0.5 for Starter, and 1–2 for Pro.
- **Memory MB** — memory limit for the provisioned container. Practical examples are 768, 2048, and 4096 MB.
- **Reservation MB** — memory reservation used when scheduling capacity.
- **Disk limit MB** — provisioned container disk limit for runner and application runtime data. It is not the total disk capacity of the host.
- **PID limit** — maximum process count allowed inside the container.
- **Maximum apps** — maximum applications that may use the runner.
- **Active** — whether the specification may be selected for valid plan mappings and new provisioning.

Revision is assigned by the specification lifecycle. It identifies the technical contract version. Existing containers keep the revision and snapshot captured when they were provisioned.

## Recommended initial example

```text
Name: Free Hosted Runner
Code: free
CPU: 0.25
Memory: 768 MB
Disk: 2048 MB
Maximum apps: 3
Active: Yes
```

## After saving

The specification exists, but no user receives it yet. Assign it to an enabled Hosted Runner capability on a Subscription Plan. An inactive specification cannot be selected for new valid plan mappings or provisioning; existing containers retain their snapshot.
