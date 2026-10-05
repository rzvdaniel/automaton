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
| Systemd resource group limits could not be verified or did not match | Check the requested CPU, memory, swap and PID limits against the host's systemd slice properties. Provisioning/reprovisioning stops before replacing the container when readback is missing, malformed or mismatched. Slice properties may already have changed; this failure does not restore their previous values. Correct the host configuration and retry reprovisioning. |
| Runner reports an API certificate validation error | Verify the API certificate matches its configured hostname and is trusted by the Runner host or container. Install the issuing CA in that environment for development certificates; do not disable certificate validation. |

This feature is still in active development. Development environments may require manual host preparation, and incomplete legacy hosted-runner configuration is not silently repaired or given a fallback specification.

## Workflow and custom-step execution

Workflow and custom-step execution uses durable RabbitMQ commands and authenticated HTTPS results. API and Runner versions must be released together after applying the execution-operation migration; there is no SignalR execution fallback. All supported Runner profiles need access to the configured external broker endpoint (`HostedRunnerRabbitMq:Host` and `Port`), and the API needs its trusted `RabbitMq:ManagementUrl` credentials to issue consume-only Runner credentials. API slots must share the existing Data Protection key ring to read protected execution requests/results.

If an invocation does not finish, check broker connectivity, the Runner execution-consumer logs and API access. A Runner interrupted after accepting work reports an uncertain terminal outcome on redelivery and does not automatically repeat the script. Inspect side effects before deliberately running it again. Request cancellation stops queued work and is cooperative for running workflows; the synchronous custom-step engine cannot forcibly stop an already-running script. Cancellation does not undo side effects.

Runner removal, revocation and Hosted Runner deprovisioning remove execution broker access and close unfinished operations. SignalR remains for presence/heartbeat and BYO runtime tunnels; it is not the durable management execution path.

Enrolled Runner execution endpoints require the bound Runner API key and `X-Runner-Id`. Existing unbound BYO registrations use their authenticated owner API key and the same identity header, subject to existing Runner access and privileged-profile authorization.

### Runner broker TLS

Before releasing matching API and Runner versions, configure a RabbitMQ TLS listener reachable from every Runner, including BYO hosts. Set `HostedRunnerRabbitMq:Host`, `Port` (the actual TLS listener port, commonly `5671`), and `TlsEnabled=true` in API configuration. Port `5671` alone does not enable TLS. `TlsEnabled` defaults to true; the existing port default remains `5672`, so explicitly select your TLS listener port. Workflow/custom-step credential provisioning and consumption reject disabled or missing TLS; no plaintext fallback is used.

The certificate must chain to a CA trusted by the Runner operating system and match `Host`. When connecting through a different address, set `HostedRunnerRabbitMq:TlsServerName` to the expected certificate DNS name. Install a private issuing CA into the Runner host/container trust store when needed, and keep revocation information reachable. Do not disable certificate or hostname validation. Both workflow/custom-step and Hosted Runner deployment consumers honor the supplied TLS settings. TLS errors leave commands pending for recovery rather than retrying over plaintext.

This configures the external Runner broker connection. The API's trusted RabbitMQ management endpoint and internal command-publishing connection retain their existing configuration. No listener, firewall, certificate, or production configuration is changed automatically by this release.

## API cutover tunnel recovery

Cutover publishes the latest API color to every eligible registered protocol-v5 Runner, including those disconnected or still recorded on the target color while earlier commands are queued. Platform-provisioned Hosted Runner credentials must still link to a non-deleted Hosted Runner; retained registrations left after deletion are excluded even when their keys remain active. Legacy BYO registrations remain eligible. Upgrade all connected eligible Runners before cutover. Tunnel admission can return 503 during the handoff; existing requests have a bounded 20-second grace. Hub HTTP 429 retries honor `Retry-After`; a server deadline longer than the normal 30-second backoff cap takes precedence. Wait for recovery rather than repeatedly restarting the Runner.

Manual reconciliation remains available if publication or connectivity fails. From the production checkout, use the existing `request_api_color_runner_tunnel_reconnect <source-color> <live-target-color> true` helper in `scripts/prod-blue-green-common.sh`. The source must be a running API revision supporting that endpoint; an older rollback image need not provide it. Restore the intended live selector before requesting reconciliation, keep both API colors running if publication fails, and retry after correcting broker/API connectivity. This is tunnel recovery, not a management drain.

The reviewed automatic recovery follow-up is released. Rapid rollback/rollforward with queued earlier targets passed without manual reconciliation for the installed production cohort; see the repository roadmap for its release evidence and separate limitations.

Revoked Infrastructure Runner records are retained for history but excluded from cutover and compensation commands once their bound API key is inactive. Eligible disconnected Runners still receive the latest target; do not delete their queues to work around a reconnect delay.
