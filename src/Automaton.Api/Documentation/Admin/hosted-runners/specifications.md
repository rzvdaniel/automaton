---
title: Specifications
order: 30
---

# Hosted Runner Specifications

A specification is the technical runtime contract for a container. It defines CPU, memory, memory reservation, disk, PID, and maximum-app limits.

The **Code** is a stable identifier used for configuration and operations; the **Name** is the administrator-facing display label. For example:

```text
Name: Pro Hosted Runner
Code: pro
CPU: 1 core
Memory: 2 GB
Disk: 10 GB
Maximum apps: 10
Active: Yes
```

Inactive specifications cannot be selected for new provisioning or reprovisioning. A specification can be shared by multiple subscription plans.

Specifications have revisions. When a runner is provisioned, its active specification snapshot records the exact resource contract and revision used. Editing a specification or changing a plan mapping does not silently resize or move an existing container. Explicitly reprovision affected containers when the new contract should take effect.
