# App management improvements: Stage 1 audit

Date: 2026-07-27

## Scope and conclusions

This audit covers the generic app model, tags, the Studio Apps page, Runner
assignment, app-card metadata, and CLI deployment identity. It intentionally
does not change runtime behavior.

The requested experience can be implemented without `ApplicationKind`,
multi-Runner assignment, or deployment-instance concepts. The existing
`WebApp.ExecutionRunnerId` remains the single execution target. Tags must remain
organization metadata and must not participate in Runner authorization.

The smallest useful direction is:

1. enrich the app-list response with a compact, read-only Runner and deployment
   projection;
2. normalize the existing tag string at API boundaries and expose parsed tags
   to Studio without introducing a tag authorization model;
3. add a separate `AppStatusSummary` resource for app-published display status;
4. add a separate, secret-free CLI targets file whose aliases resolve to
   independent app IDs while reusing the existing deploy pipeline.

## Current model

### App and tags

- The persisted app entity is `WebApp`, not `Application`.
- A `WebApp` has one nullable `ExecutionRunnerId`.
- Publication, profile, thumbnail, category, and tag metadata live directly on
  `WebApp`.
- Tags are stored in the nullable `WebApps.Tags` text column as one
  comma-separated string and are returned unchanged as `AppModel.Tags`.
- There is no tag entity, join table, workspace tag catalog, canonical casing,
  per-tag validation, or duplicate elimination.
- The general update path assigns `app.Tags` unchanged. The publish path only
  trims the whole string. Import, duplicate, and fork flows copy the string.
- Public marketplace search performs a case-insensitive substring match against
  the whole tag string. It is not token-aware.

Therefore tags are **not normalized** today. Values such as `infra`,
` Infra `, and `INFRA` can coexist semantically, and substring matching can
produce false positives.

### Filtering and Apps page

- `GET /api/apps` returns all apps allowed by the current personal/project
  authorization context. Its only query filter is `projectId`.
- Studio stores that response in the apps Pinia store.
- Search and published/draft filtering are entirely client-side. Search covers
  name and description only; there is no tag, Runner, deployment-health, or
  grouping filter.
- `AppListView.vue` contains the AI prompt/model form, `Create with AI`,
  `Start from scratch`, Import, Templates, and How to Build content.
- A grid/list toggle exists in state and controls, but the current My Apps
  rendering always uses the same card grid. There is no separate list rendering
  implementation.
- Templates are backed by `appsService.getTemplates()` and the existing fork
  flow. Import is backed by the existing archive import flow, so both are real
  features rather than placeholders.

### App-card data source

`AppCard.vue` receives one `App` object loaded from `GET /api/apps`. The list
DTO currently includes:

| Metadata | Available on app list? | Notes |
| --- | --- | --- |
| App name | Yes | `name` |
| Thumbnail | Yes | `thumbnailUrl` plus screenshots |
| Tags | Yes | Raw comma-separated `tags` string |
| Publication state | Yes | `isPublished`, `publicUrl`, `publishedAt`, authentication/access fields |
| Assigned Runner | Partial | `executionRunnerId` only; no name or purpose |
| Runner online state | No | Can reuse `RunnerPresenceEvaluator`, but it is not projected into the app list |
| Last deployment time | Partial | Frontend/backend `runtimeDeployedAtUtc` comes from filesystem storage metadata |
| Deployment result/status | No | No generic deployment record/result is projected |
| Latest Action execution | No | `AppModuleExecutions` already stores status and timestamps per app, but the list does not query it |
| Generic status summary | No | No model or endpoint exists |

The card currently renders publication state, thumbnail, summary, project
linkage, update time, and Open/Edit/menu actions. It does not render tags,
Runner details, deployment health, or Action status.

The existing `Updated` value is general app metadata time and must not be
presented as deployment time.

### Runner assignment and authorization

- `ExecutionRunnerId` is the single app-to-Runner association.
- Runner presence is already derived consistently from connection ID and
  heartbeat through `RunnerPresenceEvaluator`.
- User/project Runner visibility is implemented by
  `ResourceAuthorizationService.GetRunnersQueryByContext`.
- The dedicated `PUT /api/apps/{id}/runner` path checks app update permission,
  applies hosted Runner capacity rules, audits the change, and currently rejects
  administrator-required/Infrastructure Runners for all callers.
- The broader app update path assigns `ExecutionRunnerId` directly from the DTO
  and does not apply the dedicated assignment checks. This is an authorization
  and invariant gap to close before enabling administrator Infrastructure
  Runner assignment.
- Tags are not consulted by the current Runner rules, which is the correct
  security boundary.

Stage 6 should centralize every assignment path behind one service/policy:
ordinary callers may select only eligible visible Runners; authorized
administrators may additionally select eligible Infrastructure Runners in
their admin scope. Direct DTO updates must not bypass that policy.

### Deployment and execution history

- Runtime availability and the last frontend/backend deployed timestamps are
  already stored in per-app filesystem metadata and mapped into `AppModel`.
- That metadata records availability/time, not a durable success/failure
  deployment history.
- `AppModuleExecution` is a durable, indexed source for the latest Action
  execution status and timestamps. It represents Action execution, not the
  overall frontend/backend deployment result, so the two concepts should remain
  distinct.
- Hosted Runner provisioning/deployment records are infrastructure lifecycle
  records and are not a generic app deployment history.

For the first Apps-page redesign, reuse runtime timestamps and availability.
If a durable generic deployment result is required, introduce a generic app
deployment record/event rather than interpreting Action or hosted-runner
records as deployment status.

## Current CLI identity and deployment flow

- `automaton.json` contains source/build/deploy configuration. Legacy
  `appId`/`server` fields remain readable for compatibility.
- Current app identity is normally selected by a named project environment in
  `.automaton/environments/<name>.json`; each environment config uses the
  `GlobalConfig` shape and may contain server, API key, and app ID.
- `.automaton/` is added to `.gitignore`, so named environment identity and
  credentials are local rather than a shareable multi-target manifest.
- `automaton deploy --env <name>` resolves one app ID and server, optionally
  prepares/assigns one Runner, then invokes `DeployService`.
- `DeployService` validates, runs the configured frontend build, packages
  source/runtime content, creates a snapshot, uploads, and triggers backend
  deployment. It is currently a single-target orchestration unit and builds and
  packages inside that unit.

Reusable pieces include configuration discovery, context resolution, Runner
preparation, validation checks, archive exclusion rules, package upload,
snapshot creation, publish/open URL handling, JSON/non-interactive output, and
the existing deploy tests.

For multi-target deployment, a new tracked `automaton.deployments.json` should
contain aliases and app IDs only (plus a non-secret environment/server-profile
selector if needed). Credentials must continue to resolve from environment,
local named environment, or global config. Build/package preparation should be
separated from per-target upload/deploy so one immutable package can be reused
across compatible targets.

## Proposed minimal schema and API changes

### Stage 2: Apps-page list projection

No database migration is required for the initial page redesign.

Extend the app-list DTO (or add a purpose-built `AppListItemDto`) with:

- `runner`: nullable `{ id, name, purpose, connectivityStatus }`;
- `deployment`: frontend/backend runtime availability and latest deployed time,
  with an explicit `result` only if backed by a real generic deployment source;
- `latestActionExecution`: optional `{ status, completedAtUtc }` only where it
  adds useful list-level signal.

Prefer an async, set-based list query. The current synchronous list method
performs per-app filesystem metadata reads and should not gain more per-app
database queries.

### Stage 3: tag normalization and UX

Keep the existing `WebApp.Tags` column initially to avoid an unnecessary schema
migration. Add one server-side normalizer used by update, publish, import,
duplicate, and fork boundaries:

- split on commas;
- trim each value;
- reject/omit empty values;
- compare case-insensitively and remove duplicates;
- enforce conservative per-tag and tag-count limits;
- persist a stable canonical representation.

Expose `tags: string[]` on the purpose-built list/settings DTO while retaining
the legacy string during a compatibility window if other clients depend on it.
Build autocomplete options by taking distinct normalized tags from apps already
visible in the caller's authorization context. Filtering/grouping can remain
client-side for the expected personal/project app counts; no tag query or index
is required initially.

### Stage 4: generic status summary

Add a separate one-to-zero-or-one `AppStatusSummary` entity keyed by
`ApplicationId`, with:

- `Status`;
- `Summary`;
- `MetricsJson`;
- `UpdatedAtUtc`;
- `ExpiresAtUtc`.

Use stable string constants for `healthy`, `warning`, `critical`, and `unknown`.
Validate status, text lengths, at most four metrics, and bounded TTL on write.
Store plain validated text and render it as text in Studio; do not accept HTML.

Add authenticated endpoints to upsert and clear the calling app's own summary.
Authorization must bind an app-scoped credential to the route app ID rather than
trusting an app ID in the payload. Include a derived read projection in app-list
DTOs; expired data is returned/rendered as stale and `unknown`. This metadata
must not affect authorization, publication, or Runner eligibility.

### Stage 5: CLI target manifest

Add `automaton.deployments.json` with a typed, validated alias-to-target model.
Entries contain no credentials and resolve to independent existing app IDs.
Add repeatable `--target`, mutually exclusive `--all-targets`, and optional
`--continue-on-error`.

Refactor deployment into:

1. shared source validation/build/package preparation;
2. sequential per-target context authorization, snapshot, upload, and deploy;
3. aggregate result rendering and exit-code calculation.

Reject unknown aliases and duplicate app IDs before deploying. Keep the existing
no-target `automaton deploy` path behavior unchanged.

## Reviewed implementation sequence

1. **Apps page redesign:** introduce the list projection, remove the unsupported
   AI creation panel, preserve functional Create/Import/Templates flows, and
   implement real card/list views plus search/status/Runner/publication controls.
2. **Tags UX:** add centralized normalization, array DTOs, multi-select filtering
   with match-any semantics, tag grouping, editing, and visible-scope
   autocomplete. Verify tags have no security effect.
3. **Generic status summary:** add the separate entity, scoped write/clear API,
   stale derivation, list projection, card/list rendering, and documentation.
4. **CLI multi-app deployment:** add the secret-free target manifest, reuse one
   compatible build/package, deploy sequentially, and aggregate failures.
5. **Infrastructure assignment UX:** centralize Runner assignment policy, close
   the general-update bypass, allow only authorized admin-scoped Infrastructure
   assignment, add warnings/badges derived from Runner purpose, and audit it.
6. **Monitoring app template:** build a private, read-only example that publishes
   the generic summary and is deployable as one app ID per target alias.
7. **Operational polish:** add generic offline/failed/stale/unassigned indicators
   and optional sorting/preferences without automatic remediation.

Each stage remains a separate branch and review boundary. Nothing should deploy
automatically.

## Verification for this audit

This stage changes documentation only. Findings were verified by tracing the
entity/DTO mappings, API list and update paths, Studio store/view/card, Runner
presence and authorization services, CLI configuration resolution, deploy
command/service, migrations, and existing test locations. No build or test suite
is required for a documentation-only change.
