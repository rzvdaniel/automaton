---
title: Seeded Configuration
order: 10
---

# Seeded Configuration

A new installation seeds this initial configuration:

- **Free** subscription plan.
- **Free Hosted Runner** specification with stable code `free`.
- **Free plan → Free Hosted Runner** mapping.
- Hosted-runner provisioning enabled globally.

Seeding does not necessarily create a runner container. A container is created when the relevant user flow requests one, such as an application deployment that needs a managed runtime or an administrator manually provisioning a runner.

The Free plan is an initial development product. Additional plans can share the same specification or reference their own specifications as paid tiers are introduced.
