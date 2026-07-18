---
title: Subscription Plans
order: 40
---

# Subscription Plans

Hosted-runner entitlement is stored in the plan capabilities contract:

```text
HostedRunner.Enabled
HostedRunner.Visibility
HostedRunner.SpecificationId
```

When `Enabled` is true, `SpecificationId` is required and must reference an active specification. Plan create, plan update, plan assignment, and the final provisioning step validate this rule.

Visibility is independent from entitlement. The supported values describe who should see or receive the plan:

- `disabled` — not publicly visible.
- `testUsersOnly` — available for internal or test users.
- `public` — available as a public product.

The effective subscription plan resolves the specification used for provisioning. This keeps commercial product configuration in the plan while keeping CPU, memory, disk, and application limits in the reusable technical specification.
