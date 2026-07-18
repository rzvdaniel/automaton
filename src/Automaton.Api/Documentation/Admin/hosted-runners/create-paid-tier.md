---
title: Create a Paid Tier
order: 60
---

# Create a New Paid Tier

Use a test-first rollout for a new commercial runner tier.

## 1. Create the specification

```text
Name: Pro Hosted Runner
Code: pro
CPU: 2
Memory: 4096 MB
Disk: 20480 MB
Maximum apps: 20
Active: Yes
```

## 2. Create the plan

```text
Name: Pro
Description: For power users and small teams
Price: $30/month
Active: Yes
Default: No
```

Configure Hosted Runner as Enabled = Yes, Visibility = `testUsersOnly`, and Specification = Pro Hosted Runner.

## 3. Configure billing

Enter the Stripe Product ID and Price ID when billing integration is ready, then save the plan.

## 4. Test and release

1. Grant hosted-runner test access to an internal user.
2. Assign the Pro plan.
3. Provision or reprovision the user’s runner.
4. Validate usage, capacity, snapshot, and runtime connectivity.
5. Change Visibility from `testUsersOnly` to `public` when the tier is ready for normal plan flows.
