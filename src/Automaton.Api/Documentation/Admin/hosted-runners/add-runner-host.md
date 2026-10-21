---
title: Infrastructure Runner Hosts
order: 30
---

# Infrastructure Runner Hosts

Runner Host records support Infrastructure Runner validation and operations, plus lifecycle and storage work for existing legacy Hosted Runner records. They are not required to install a customer Machine Management Runner and do not provision per-user Runner containers.

For new customer workloads, follow [Environments and Runner relationships](./deployment-targets.md) and assign an installed Machine Management Runner to the app or environment.

## Install or inspect a host

1. Open **Admin → Runners → Runner Hosts**.
2. Select **Install Infrastructure Runner** for the target server and run the generated installer with `sudo`.
3. Wait for enrollment, then run **Validate** and review the reported host details.
4. Approve the host after successful validation when approval is required.

Infrastructure Runner validation checks Docker access and the host capabilities needed by supported infrastructure operations. Approval does not create a Hosted Runner container or assign an application.

## Existing Hosted Runner storage

Some existing Hosted Runner records may depend on their current host-local Docker volumes, XFS project quotas, or recorded storage allowances. This cleanup does not move or delete those records or volumes. Do not reformat, move Docker's data root, or change a legacy storage quota as part of installing a Machine Management Runner. New Machine Management installations use their own Docker/workspace configuration and do not require an Infrastructure Runner Host record.
