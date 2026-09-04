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

When the replacement Runner connects, Automaton uses enrollment history to rebind accessible application targets from earlier Runner identities of the same Hosted Runner. This includes non-primary targets. Runtime deployment markers, target status, and deployment operation state are preserved. Automaton then refreshes runtime routes using the replacement identity. Verify an existing application URL after the Runner is online.

This identity repair does not move applications from another Hosted Runner or a self-hosted Runner. Ordinary Runner changes still require the deployment-target cleanup workflow.

Changing a plan does not silently resize or move an existing running container. The existing container keeps its active snapshot, placement, port, and endpoint until explicit reprovisioning.

The active specification snapshot is promoted only after provisioning succeeds. However, a failed reprovision currently leaves the runner in a failed state and may leave the newly selected placement fields recorded in the database. The existing container is not automatically restored by this workflow. Treat the upgrade as incomplete, review the failure, and use administrative recovery or another explicit reprovision attempt as appropriate.
