---
title: Drain and Remove a Host
order: 80
---

# Drain and Remove a Host Safely

1. Disable scheduling on the host.
2. Confirm that no new containers are assigned there.
3. Review the existing containers on the host.
4. Reprovision or migrate them to other eligible hosts.
5. Confirm that the runner count is zero.
6. Revoke the Infrastructure Runner.
7. Remove or archive the host record.

Disabling scheduling prevents new placements but does not move existing containers. Existing containers must be explicitly reprovisioned before the host can be safely removed.
