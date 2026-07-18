---
title: Troubleshooting
order: 100
---

# Troubleshooting

| Symptom | Checks |
| --- | --- |
| No eligible host | Confirm active status, validation, approval, scheduling, connectivity, and available capacity. |
| Insufficient capacity | Check CPU, memory, and disk after reserved resources and existing allocations. |
| Infrastructure Runner offline | Check the runner service, enrollment, API connectivity, and runner name. |
| Host not validated | Run validation and inspect reported metadata and health details. |
| Scheduling disabled | Enable scheduling on the approved host. |
| Plan capability disabled | Enable `HostedRunner.Enabled` on the effective plan. |
| Missing specification | Select an active specification on the enabled plan. |
| Inactive specification | Activate it or select another active specification. |
| Global provisioning paused | Enable **Allow hosted runner provisioning** in the Specifications tab. |
| Container provisioned but Runner not connected | Check deployment result, container logs, registration, and API connectivity. |

This feature is still in active development. Development environments may require manual host preparation, and incomplete legacy hosted-runner configuration is not silently repaired or given a fallback specification.
