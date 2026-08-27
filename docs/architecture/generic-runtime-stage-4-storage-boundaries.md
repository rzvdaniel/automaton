# Stage 4: Workspace and storage boundaries

Stage 4 gives the runner explicit, stack-neutral filesystem ownership and
lifecycle boundaries. The application can organize everything below its source
workspace however it needs; Automaton does not infer a language, framework,
package manager, or frontend/backend role from that content.

## Canonical runner layout

The configured `AUTOMATON_WORKSPACE_ROOT` is the infrastructure-owned mount
boundary. Within it, the runner uses the following roots:

```text
workspace/
  apps/{appId}/targets/{targetId}/
    workspace/source/       agent/application-owned project source
    artifacts/              build and deployment artifacts
      current/              promoted runtime artifact
      candidates/{id}/      isolated candidate artifact
    data/                   durable application data for this target's app
    cache/                  disposable build/cache state
      legacy-python/venv/   deferred controller bridge cache only
    logs/{runtime-id}/      runtime stdout/stderr, outside artifacts
    automaton/              runner metadata and snapshots
      deployment.json
      snapshots/
    runtime/                reserved legacy execution-state boundary
```

The app-level `data/{appId}` root is durable application data and is separate
from a deployment target. `workspace/source`, artifacts, cache, logs, and
Automaton metadata belong to the deployment target and may be recreated or
removed according to deployment policy. Runtime log directories are keyed by
runtime identity so replacing an artifact does not overwrite the previous
runtime's diagnostic files.

The `ApplicationStorageSpec` contract selects a data directory and named mount
directories relative to the durable app-data root. The runner creates those
directories and exposes their resolved paths through generic environment
variables (`AUTOMATON_APP_DATA_DIR` and
`AUTOMATON_STORAGE_{NAME}`). Names are environment-safe identifiers and all
paths are validated as relative, contained paths. Existing path components are
also checked for reparse-point/link escapes.

## Ownership and lifecycle

- The agent/application owns files inside `workspace/source`.
- Automaton owns the location and lifecycle of workspace, artifact, cache,
  log, metadata, and durable-data roots.
- Runtime recreation stops/removes target-owned runtime state, artifacts, and
  cache without deleting durable app data.
- Deployment cleanup can independently remove target state, artifacts, cache,
  source, or durable application data. The explicit persistent-data operation
  is not part of ordinary runtime cleanup.
- Candidate artifacts are isolated from `artifacts/current` until runtime
  readiness and durable promotion succeed.

## One-way migration

`RunnerStorageMigrationService` runs before runtime recovery. It moves the
previous target layout into the canonical roots once:

- `source` becomes `workspace/source`;
- `runtime/application` becomes `artifacts/current/application`;
- `runtime/candidates` becomes `artifacts/candidates`;
- old runtime logs become `logs`;
- `venv` becomes `cache/legacy-python/venv`;
- `versions` and `deployment.json` become `automaton/snapshots` and
  `automaton/deployment.json`;
- legacy app-local data becomes the durable app-data root;
- artifact paths in migrated deployment metadata are rewritten to the new
  roots.

The migration preserves source, durable data, and usable deployment state. It
does not make the old directory names a fallback for generic build or runtime
execution. A failed migration prevents the runner from continuing with an
ambiguous layout.

## Scope boundary

The generic build and runtime paths use only the canonical roots and structured
application contract. The existing `controller_runner.py`, FastAPI tunnel,
source/backend helpers, and legacy Python virtual-environment cache remain
behind the explicitly deferred Action/controller bridge tracked in the Stage 0
inventory. The fixed frontend gateway and controller transport are not used by
the generic runtime contract and remain later-stage work.
