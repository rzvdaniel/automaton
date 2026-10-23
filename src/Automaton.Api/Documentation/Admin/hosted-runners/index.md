---
title: Runner Administration
order: 20
---

# Runner Administration

New workloads should use a customer-installed Machine Management Runner assigned to an app or environment. Per-user Hosted Runner creation and provisioning have been retired; plan assignment, billing, or signup no longer creates a Runner container.

- [Environments, deployment targets, and Runner assignments](./deployment-targets.md)
- [Runner Hosts and Infrastructure Runner operations](./infrastructure-runners.md)
- [Add or inspect an Infrastructure Runner Host](./add-runner-host.md)
- [Hosted Runner legacy operations and troubleshooting](./troubleshooting.md)
- [Pause public registration](./public-registration.md)

Existing Hosted Runner records and their persisted data remain in the database. The remaining admin view supports read-only legacy record inspection. It does not create or reprovision per-user Runner containers. Existing records are not upgraded by this change.

Hosted Runner storage reporting/verification, suspension/resume, grace-period extension and enforcement reset are retired. Stored storage and enforcement values are historical; they are no longer refreshed. Account-storage locality is no longer adopted or verified. This does not resume previously suspended workloads; retirement or recovery of legacy installations remains a separate operator decision. Machine Management uses configured container limits and free-disk admission instead of account storage allowances.

Connecting or reconnecting a Runner does not automatically assign apps or transfer their targets from a previous Hosted Runner identity. Use [Runner assignments](./deployment-targets.md) to select the machine explicitly. An existing deployed target still requires its normal cleanup before changing Runner.

Infrastructure host storage reporting and its XFS image-store validation are retired. Host storage values are historical, not current capacity or placement evidence. New typed container work requires an assigned Machine Management Runner; Standard Hosted Runners no longer relay it through an Infrastructure Runner. Machine workspace telemetry and workload safeguards remain available.

Hosted restart/delete and automatic registration/reconnect/lifecycle mutation are retired. Stored lifecycle and deployment statuses are historical, not current maintenance instructions. No containers, volumes or stored records are deleted by this source cleanup. Machine Management updates remain unsupported; retirement, export and manual moves of existing workloads need a separate operator decision.

Retained Hosted assignments cannot pass deployment readiness: assign a Machine Management Runner explicitly. The API no longer generates direct routes to historical Hosted endpoints, and current Runners refuse the retired Hosted startup identity. This does not migrate or stop an existing installation; keep any retirement or export decision separate.
