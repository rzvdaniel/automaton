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

Existing Hosted Runner records and their persisted data remain in the database. The remaining admin view supports legacy record inspection, restart and deletion. It does not create or reprovision per-user Runner containers. Existing records are not upgraded by this change.

Hosted Runner storage reporting/verification, suspension/resume, grace-period extension and enforcement reset are retired. Stored storage and enforcement values are historical; they are no longer refreshed. Account-storage locality is no longer adopted or verified. This does not resume previously suspended workloads; retirement or recovery of legacy installations remains a separate operator decision. Machine Management uses configured container limits and free-disk admission instead of account storage allowances.

Connecting or reconnecting a Runner does not automatically assign apps or transfer their targets from a previous Hosted Runner identity. Use [Runner assignments](./deployment-targets.md) to select the machine explicitly. An existing deployed target still requires its normal cleanup before changing Runner.
