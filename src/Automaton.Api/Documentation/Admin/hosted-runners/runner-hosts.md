---
title: Runner Hosts
order: 20
---

# Runner Hosts

Runner Hosts are infrastructure capacity. A host becomes eligible for hosted-runner scheduling only after its infrastructure connection, preparation, validation, approval, and scheduling state are ready.

## Onboarding checklist

1. Add or discover the server.
2. Install the Infrastructure Runner.
3. Complete runner enrollment.
4. Prepare and validate the host.
5. Approve the host when approval is required.
6. Enable hosted-runner scheduling.

The host must report the required health and metadata, have a usable provisioning transport, and have enough unreserved CPU, memory, and disk for the requested specification. The Infrastructure Runner must remain connected for Infrastructure Runner-based provisioning. Network access, Docker, the configured runner data directories, and the selected provisioning transport must be available on the server.

Capacity includes both currently used and reserved resources. A host is not selected when any requested CPU, memory, or disk limit would exceed the remaining capacity.

Use **Install Infrastructure Runner** to generate installation material, then use the host actions to validate, approve, enable or disable scheduling, and replace or revoke the Infrastructure Runner connection.
