---
title: Configure a Subscription Plan
order: 20
---

# Create or Configure a Subscription Plan

## Where

Administration → **Subscription Plans** → **New Plan** or edit an existing plan.

Configure the commercial fields first:

- Name
- Description
- Price
- Active
- Default
- Visibility
- Stripe product and price identifiers, when applicable

## Hosted Runner capability

Hosted-runner entitlement is configured separately from the commercial fields:

```text
HostedRunner.Enabled
HostedRunner.Visibility
HostedRunner.SpecificationId
```

- **Enabled** — users on this plan are entitled to a managed runner. When disabled, new provisioning is rejected and removing the plan can disable the user’s managed runners according to the current assignment flow.
- **Visibility** — controls who can see or receive the plan:
  - `public` — visible in normal plan flows;
  - `testUsersOnly` — available to users with hosted-runner test-plan access;
  - `disabled` — not presented as an available offer.
- **Specification** — the technical runtime contract. For example, Free → Free Hosted Runner and Pro → Pro Hosted Runner.

The current UI resolves the specification from the user’s effective subscription plan. It does not provide a separate per-provision host or specification override.

## Validation

When Hosted Runner is enabled:

- a specification is required;
- the specification must exist;
- the specification must be active.

For the initial installation, use: **Free**, Active = Yes, Default = Yes, Hosted Runner Enabled = Yes, Visibility = Public, Specification = Free Hosted Runner.

## After saving

The plan is eligible, but users receive it only when it is their effective default plan, an administrator assigns it directly, or subscription billing logic assigns it.
