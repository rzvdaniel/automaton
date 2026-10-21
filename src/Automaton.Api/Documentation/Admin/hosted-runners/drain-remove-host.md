---
title: Drain and Remove a Host
order: 80
---

# Drain and Remove a Host Safely

1. Review the existing Hosted Runner records and their host-local data.
2. Keep the host and its Docker storage in place while any required legacy workload or data remains there.
3. Use the existing per-record delete operation only after the data disposition is deliberately chosen.
4. Revoke the Infrastructure Runner when the host no longer needs infrastructure operations.
5. Remove or archive the host record only after its remaining consumers are understood.

Per-user Hosted Runner provisioning and migration to another host are no longer supported. This procedure does not migrate data or workloads. New workloads should use a separately installed and assigned Machine Management Runner.
