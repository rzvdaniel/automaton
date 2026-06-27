# Runner Runtime Access Simplification Plan

Date: 2026-06-27

## Summary

The current app access model mixes too many concerns in one place:

- Studio routes
- app runtime routes
- private app launch and auth
- public app publishing
- runtime script injection
- local development host differences
- API-side file serving versus runner-side execution

This creates brittle behavior and unnecessary special cases. The recent runner workspace migration makes it possible to simplify the model instead of layering more compatibility logic onto it.

This plan proposes a cleaner target:

- Studio remains a separate control-plane UI.
- App runtime becomes a separate runtime surface.
- Restricted versus public becomes an authorization concern, not a host-shape concern.
- The runner becomes the canonical home for deployed frontend and backend runtime files.

## Core Decision

Target architecture:

- Studio host serves Studio only.
- API remains the control plane for auth, metadata, publish state, runner assignment, and orchestration.
- Runner becomes the runtime plane for frontend assets and backend execution.
- Runtime URLs are explicit and stable.

Target mental model:

- `studio` is for building and managing apps.
- `runtime` is for opening and using apps.
- `restricted` and `public` are access modes on top of runtime delivery, not separate delivery architectures.

Recommended user-facing model:

- Studio preview: builder-facing authenticated preview inside Studio
- restricted runtime: app runtime for authenticated users who already have app access
- public runtime: published runtime for broad access

## Problems In The Current Model

### 1. Host Responsibilities Are Blurred

The current implementation lets the API behave as:

- API control plane
- private runtime host
- published runtime host fallback
- local-development compatibility host

That produces confusing URL behavior and weak separation of concerns.

### 2. Restricted And Public Use Different Serving Paths

Restricted and published apps currently diverge too early:

- different route shapes
- different lookup paths
- different auth behavior
- different local-host expectations

This makes the code harder to reason about and increases regression risk.

### 3. Local Development Needs Special Heuristics

The CLI and server need local host/port special casing because:

- Studio runs on one host
- app runtime runs on another
- the control-plane server is used as both API origin and runtime origin

This is workable but not clean.

### 4. Frontend Runtime And Backend Runtime Are Split Conceptually

The current platform still behaves as if:

- frontend runtime is mainly API-hosted
- backend runtime is runner-hosted

That is the root reason runtime access is awkward. The user experiences one app, but the platform serves it from two different ownership models.

## Target Model

### Runtime URL Shape

Use one runtime host and two explicit route families:

```text
/a/{appId}/...    restricted runtime
/p/{slug}/...     public runtime
```

Alternative route naming is acceptable, but the rules should remain:

- app-id route for private runtime
- app-id route for restricted runtime
- slug route for public runtime
- identical runtime serving stack behind both

### Runtime Serving Rules

Both restricted and public runtime should:

- serve frontend files from `runtime/frontend`
- use the same SPA fallback logic
- inject the same runtime bridge/config contract
- proxy backend calls through the same runtime gateway shape

The only difference should be authorization and app identity lookup.

### Ownership Rules

- API owns metadata and access decisions.
- Runner owns deployed app runtime state.
- Studio never becomes an app runtime host.
- Runtime frontend and backend deploy together as one unit.

### Access Policy Model

Do not treat runtime access as only a visibility toggle.

Use three distinct access surfaces:

- Studio preview
  - for builders/editors inside Studio
  - authenticated control-plane experience
- restricted runtime
  - for authenticated users who already have access to the app
  - access comes from personal ownership or project membership
- public runtime
  - for published apps reached by slug
  - may be anonymous or authenticated depending on app policy

Important clarification:

- organization membership by itself is not the runtime access rule
- app access should flow from app/project authorization
- runtime should trust app ACL decisions, not a separate “private app” concept

## Recommended Phase Order

### Phase 3A - Define Runtime Boundaries

Goal:

- Make host responsibilities explicit before more runtime features are added.

Scope:

- Declare Studio as control-plane-only.
- Declare runtime as app-serving-only.
- Remove the assumption that Studio host also needs to explain or emulate runtime routes.
- Update docs and CLI messaging to reflect the split.

Exit criteria:

- Host responsibilities are documented and consistent in code comments, CLI output, and routing assumptions.

Verification:

- Manual review of runtime URL generation
- targeted CLI tests around output URLs

### Phase 3B - Introduce A Unified Runtime Route Model

Goal:

- Replace divergent restricted/public route logic with one runtime-serving pipeline.

Scope:

- Add one internal runtime file-serving service that accepts:
  - app identity
  - runtime path
  - backend API base path
  - access mode
- Make both private and public routes use that shared service.
- Keep lookup and auth separate from the file-serving logic.

Exit criteria:

- restricted and public runtime requests converge into one file-serving pipeline after identity/auth resolution.

Verification:

- targeted API tests for restricted and public HTML entry requests
- targeted API tests for static asset requests
- targeted API tests for auth failures returning expected HTML or status codes

### Phase 3C - Move Runtime Frontend Ownership To The Runner

Goal:

- Stop treating API-local storage as the primary frontend runtime home for migrated apps.

Scope:

- Use runner-owned `runtime/frontend` as the source of truth for deployed frontend files.
- Keep API as orchestrator only.
- Add API-to-runner runtime file access contract where needed.
- Do not move Studio source editing in the same step unless already covered by the workspace plan.

Exit criteria:

- migrated apps no longer depend on API-local runtime frontend storage as the primary deploy target.

Verification:

- deploy tests
- runner extraction tests
- runtime asset serving tests

### Phase 3D - Make Private/Public A Pure Access Layer

Goal:

- Reduce restricted/public differences to auth and metadata.

Scope:

- Restricted app route resolves app by `appId`, validates access, then serves runtime.
- Public app route resolves app by `slug`, validates publish/access state, then serves runtime.
- After resolution, both use the same runtime-serving stack.

Exit criteria:

- no duplicate restricted/public HTML-serving implementations remain
- no separate runtime injection rules remain

Verification:

- restricted route tests
- public route tests
- unpublished/unauthorized/not-found behavior tests

### Phase 3E - Add A Runner Runtime Gateway

Goal:

- Put runtime frontend serving and backend proxying behind the runner for migrated apps.

Scope:

- Add or extend runner web host.
- Serve only `runtime/frontend`.
- Proxy app backend HTTP traffic to the managed backend process.
- Keep strict allowlists for public file access.

Important constraint:

- Never expose `source`, `runtime/backend`, `versions`, `logs`, `data`, `cache`, `runner`, or app metadata files.

Exit criteria:

- runner can safely serve an app’s runtime frontend and proxy its backend API

Verification:

- runner gateway tests
- file allowlist tests
- backend proxy tests

### Phase 3F - Move Edge Routing To Runtime Hosts

Goal:

- Stop relying on Studio/API host ambiguity for app opening.

Scope:

- Route runtime traffic to runtime host only.
- Route Studio traffic to Studio host only.
- Keep local development explicit:
  - Studio dev server URL
  - API/control plane URL
  - runtime URL
- Remove ad hoc host guessing once explicit config is available.

Exit criteria:

- runtime URLs and Studio URLs are generated from explicit host configuration
- local testing does not require inference-heavy URL rewriting

Verification:

- CLI URL output tests
- local compose smoke tests
- publish/open flow smoke tests

## Design Guardrails

### 1. Studio Must Not Be A Runtime Host

- Studio is for editing and management.
- App runtime should not be coupled to Studio SPA routes.

### 2. Private/Public Are Not Separate Storage Models

- Both restricted and public runtime must use the same runtime file layout.
- Only identity resolution and access policy may differ.

### 3. Runner Owns Runtime

- Deployed frontend and backend runtime should live together in the runner workspace.
- Do not preserve split ownership just because old code expects it.

### 4. API Is Control Plane Only

- API may authorize and orchestrate.
- API should not remain a shadow runtime file host long term.

### 5. Runtime Routes Must Be Explicit

- Avoid further localhost heuristics and host guessing.
- Prefer explicit `AUTOMATON_STUDIO_BASE_URL`, `AUTOMATON_API_BASE_URL`, and runtime host settings in local development.

## Benefits

- fewer route and host special cases
- less restricted/public duplication
- cleaner local-development story
- frontend/backend deploy and serve as one app unit
- easier reasoning about ownership and failure modes
- simpler future reverse-proxy and managed-hosting design

## Tradeoffs

### Runtime Availability Depends More On The Runner

If frontend runtime moves fully to the runner, published app availability depends on runner health unless a higher-level managed routing layer exists.

That is acceptable if the platform direction is runner-owned runtime.

### Publishing Becomes More Runtime-Oriented

Publishing stops being mostly a file-copy concern and becomes a runtime-routing concern.

That is a larger architectural shift, but it matches the new workspace direction.

### More Gateway Work Is Required

The simplification moves complexity out of route sprawl and into:

- runner gateway
- reverse proxy configuration
- runtime host configuration

That is a better place for the complexity because it is infrastructure-shaped rather than duplicated app-serving logic.

## Non-Goals

- Do not redesign marketplace UX in this phase.
- Do not redesign org vanity runtime URLs yet.
- Do not add CDN-specific optimizations yet.
- Do not keep unnecessary backward compatibility if the old route model is only used in development-stage code.

## Recommended Immediate Next Steps

1. Finish the runner-owned deploy/runtime migration before adding more API-side runtime features.
2. Introduce a shared runtime-serving service in `UserAppsController` or its replacement.
3. Remove duplicated restricted/public HTML error and injection behavior.
4. Define explicit runtime host configuration for local development.
5. Implement runner gateway after runtime-serving logic is unified.
6. Cut over generated CLI output to explicit Studio/runtime host settings.

## Naming Recommendation

The current term `private app` is misleading because the actual rule is not simply “not public”.

Prefer:

- `Studio preview`
- `restricted runtime`
- `public runtime`

If route renames are deferred, keep the old paths temporarily but update internal docs and architecture language first so future work follows the correct model.

## Files Likely Affected

API:

- `src/Automaton.Api/Controllers/UserAppsController.cs`
- runtime-serving helpers and publish/access services
- private launch/auth services

Runner:

- runner web host or HTTP gateway components
- backend process manager
- runtime file access services

CLI:

- `src/Automaton.Cli/Services/CliAppUrlBuilder.cs`
- deploy/open/publish output and runtime host config handling

Docs:

- `docs/plans/managed-runner-workspace-migration-plan.md`
- runtime/deploy docs
- local development docs

## Final Recommendation

Yes, the current restricted/public access model is more complicated than it should be.

The runner-container migration is the right moment to simplify it properly:

- keep Studio separate
- make runner the runtime home
- unify restricted/public serving
- make routing explicit
- reduce restricted/public to access policy rather than architecture

That gives Automaton a cleaner long-term model and avoids spending more time stabilizing an architecture that is already pointing in the wrong direction.
