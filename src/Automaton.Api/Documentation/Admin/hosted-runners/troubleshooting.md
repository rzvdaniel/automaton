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
| Runner reports an API certificate validation error | Verify the API certificate matches its configured hostname and is trusted by the Runner host or container. Install the issuing CA in that environment for development certificates; do not disable certificate validation. |

This feature is still in active development. Development environments may require manual host preparation, and incomplete legacy hosted-runner configuration is not silently repaired or given a fallback specification.

## Workflow and custom-step execution

Workflow and custom-step execution uses durable RabbitMQ commands and authenticated HTTPS results. API and Runner versions must be released together after applying the execution-operation migration; there is no SignalR execution fallback. All supported Runner profiles need access to the configured external broker endpoint (`HostedRunnerRabbitMq:Host` and `Port`), and the API needs its trusted `RabbitMq:ManagementUrl` credentials to issue consume-only Runner credentials. API slots must share the existing Data Protection key ring to read protected execution requests/results.

If an invocation does not finish, check broker connectivity, the Runner execution-consumer logs and API access. A Runner interrupted after accepting work reports an uncertain terminal outcome on redelivery and does not automatically repeat the script. Inspect side effects before deliberately running it again. Request cancellation stops queued work and is cooperative for running workflows; the synchronous custom-step engine cannot forcibly stop an already-running script. Cancellation does not undo side effects.

Runner removal, revocation and Hosted Runner deprovisioning remove execution broker access and close unfinished operations. Other management operations still depend on SignalR, so retain the current reconnect/drain procedure until those paths are migrated.

Enrolled Runner execution endpoints require the bound Runner API key and `X-Runner-Id`. Existing unbound BYO registrations use their authenticated owner API key and the same identity header, subject to existing Runner access and privileged-profile authorization.
