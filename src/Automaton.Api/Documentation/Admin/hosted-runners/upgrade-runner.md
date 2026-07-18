---
title: Upgrade or Resize a Runner
order: 50
---

# Change a User to Another Runner Size

## Example

```text
Free plan → Free Hosted Runner
Pro plan  → Pro Hosted Runner
```

## Procedure

1. Create and activate the new specification.
2. Assign it to the target Subscription Plan.
3. Assign the target plan to the user.
4. Confirm that the user’s effective plan changed.
5. Open Runner Containers and locate the user’s existing container.
6. Choose **Reprovision**.
7. Confirm that the requested specification is pending.
8. Wait for provisioning to complete.
9. Verify that the active snapshot changed to the new revision.

Changing a plan does not silently resize or move an existing running container. The existing container keeps its active snapshot, placement, port, and endpoint until explicit reprovisioning.

If no host can fit the new specification, reprovisioning fails. The existing runtime remains authoritative until replacement succeeds; do not treat a failed reprovision as a completed upgrade.
