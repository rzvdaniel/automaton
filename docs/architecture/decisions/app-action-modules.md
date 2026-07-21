# App Action Modules

## Decision

An App is the composition, ownership, authorization, deployment, and lifecycle boundary. An Action is an executable source module addressed by `AppId + ActionKey`; it is not a standalone resource and has no definition table or independent access grants.

Action source lives under `actions/{action-key}/`. The Runner validates and stages each module independently, preserves Runner-local versions, installs dependencies, launches one process per execution, and retains temporary logs/results locally. The central database stores only `AppModuleExecution` control-plane metadata and never input, result, full logs, source, dependencies, or execution workspaces.

The CLI is the canonical authoring and deployment path. The API relays deployment packages and coordinates execution; it is not the canonical Action source store. The Runner's deployed `source/actions` snapshot is the source used for preparation and execution. Studio operates deployed runtime state and directs users to `automaton deploy` when an Action is not deployed.

Python and Bash Actions exchange one JSON value through stdin/stdout and use stderr for logs. Normal App Actions run without privileged host or Docker capabilities. Platform infrastructure operations use the same generic module protocol through a platform-owned Actions-only App, with separate admin-only orchestration and capability enforcement.
Runner-local Action versions are automatically pruned after deployment. The defaults
retain up to ten versions and 1 GiB per Action; operators can override these limits
with `AUTOMATON_ACTION_MAX_VERSIONS` and `AUTOMATON_ACTION_MAX_VERSION_BYTES`.
