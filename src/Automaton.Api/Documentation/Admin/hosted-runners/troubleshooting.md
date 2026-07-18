---
title: Troubleshooting
order: 70
---

# Troubleshooting

| Symptom | Checks |
| --- | --- |
| No eligible host | Confirm the host is active, validated, approved, scheduling-enabled, and connected through its configured transport. |
| Insufficient capacity | Check available CPU, memory, and disk after used and reserved resources are included. |
| Infrastructure Runner offline | Confirm the runner service, enrollment, API connectivity, and runner name on the host. |
| Host not validated | Run host validation and inspect the reported metadata and health details. |
| Scheduling disabled | Enable scheduling on the approved host. |
| Plan capability disabled | Enable `HostedRunner.Enabled` on the effective plan. |
| Missing specification | Select an active specification on the enabled plan. |
| Inactive specification | Activate the specification or select another active one. |
| Global provisioning paused | Enable **Allow hosted runner provisioning** in the Specifications tab. |
| Container provisioned but Runner not connected | Check the container deployment result, runner API key setup, runtime logs, and the Runner registration connection. |

This feature is still in active development. Development environments may require manual host preparation and do not preserve compatibility with incomplete legacy hosted-runner configuration. Treat plan changes and explicit reprovisioning as controlled operational actions.
