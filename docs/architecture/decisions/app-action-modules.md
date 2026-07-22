# App Action Modules

## Decision

An App is the composition, ownership, authorization, deployment, and lifecycle boundary. An Action is an executable source module addressed by `AppId + ActionKey`; it is not a standalone resource and has no definition table or independent access grants.

Action source lives under `actions/{action-key}/`. The Runner validates and stages each module independently, preserves Runner-local versions, installs dependencies, launches one process per execution, and retains temporary logs/results locally. The central database stores only `AppModuleExecution` control-plane metadata and never input, result, full logs, source, dependencies, or execution workspaces.

The CLI is the canonical authoring and deployment path. The API relays deployment packages and coordinates execution; it is not the canonical Action source store. The Runner's deployed `source/actions` snapshot is the source used for preparation and execution. Studio operates deployed runtime state and directs users to `automaton deploy` when an Action is not deployed.

Python and Bash Actions exchange one JSON value through stdin/stdout and use stderr for logs. Normal App Actions run without privileged host or Docker capabilities. Platform infrastructure operations use the same generic module protocol through a platform-owned Actions-only App, with separate admin-only orchestration and capability enforcement.

Hosted Runner lifecycle mutations are implemented by the trusted infrastructure App. The API/Worker control plane selects an approved Runner Host, records a durable operation, and requests an allowlisted infrastructure Action. The API relays the bundled platform Action to that host's authenticated Infrastructure Runner and invokes it through the generic Action protocol. Provisioning is not complete when Docker returns: the new hosted Runner must register with its fixed `HostedRunnerId`, at which point the API links it and completes the operation. Normal provisioning never executes Docker in the API or Worker containers; SSH is reserved for Infrastructure Runner bootstrap and explicit recovery.

The trusted lifecycle Actions are `provision-hosted-runner`, `reprovision-hosted-runner`, `restart-hosted-runner`, and `delete-hosted-runner`. They require both the fixed platform App identity and an online, active, validated, approved Infrastructure Runner with hosted-provisioning capability.
Runner-local Action versions are automatically pruned after deployment. The defaults
retain up to ten versions and 1 GiB per Action; operators can override these limits
with `AUTOMATON_ACTION_MAX_VERSIONS` and `AUTOMATON_ACTION_MAX_VERSION_BYTES`.
