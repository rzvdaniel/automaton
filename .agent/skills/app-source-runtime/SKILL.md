---
name: app-source-runtime
description: Use when changing Automaton app source, live/runtime files, publishing, forking, remixing, archive import/export, Studio editor file handling, or public app serving.
---

# App Source and Runtime Model

Use this skill when touching:

- app file storage
- Studio app editor file operations
- CLI deploy/pull/archive behavior
- fork/remix flows
- marketplace copying
- public app serving
- backend server tab behavior
- runner-side deployed backend files

## Core model

Automaton apps have separate editable source and deployed runtime surfaces.

- `source/frontend/` is editable frontend source.
- `source/backend/` is editable backend source.
- `live/frontend/` is deployed public frontend output.
- `live/backend/` is runner-side backend output.

Studio, CLI pull, remix/fork, snapshots, and import/export should prefer `source/`.

Public serving should use deployed frontend output only. It must not expose backend files, secrets, logs, runner state, or unsafe file types.

## Rules

- Do not manually edit `live/frontend/` or `live/backend/` as source.
- Do not copy `live/backend/` during fork/remix.
- Do not expose backend runtime files through public app routes.
- Do not copy secrets, logs, runner state, `.env`, local caches, user data, or deployment artifacts.
- Keep frontend and backend surfaces separate.
- Keep plain root HTML/CSS/JS support only for explicit no-build static apps.
- Default generated local frontend apps to `frontend/` with a build pipeline that outputs to `frontend/dist/`.
- Default generated backend apps to `backend/` when a backend is requested.

## Runtime app data

Runtime-created durable files must live under `AUTOMATON_APP_DATA_DIR`.

Use a small convention so generated apps stay predictable:

```txt
$AUTOMATON_APP_DATA_DIR/
  media/
  db/
  generated/
  cache/
  logs/
  tmp/
```

Rules:

- Treat `AUTOMATON_APP_DATA_DIR` as the only hard requirement.
- Use the listed subfolders when they fit; do not create empty folders unnecessarily.
- `media/` is the only platform-aware folder today. Files under `media/` may be referenced by app-relative public URLs such as `/media/uploads/...`.
- Prefer `db/` over a narrower folder name such as `sqlite/`.
- Do not store runtime data in `source/`, `live/`, `frontend/dist/`, `wwwroot`, or next to deployed backend controller files.

## Fork/remix rules

Forks/remixes should:

- copy portable editable source when available
- preserve attribution and lineage
- default to private draft unless product rules say otherwise
- clear runner assignment
- warn that backend execution requires the user’s own runner or a managed runner
- avoid copying `live/backend/`, secrets, logs, runner state, and user data

A sanitized `live/frontend/` snapshot may be copied only when intentionally used for immediate preview and only after unsafe files are excluded.

## Public serving rules

Public app serving should:

- serve only safe frontend runtime files
- use allowlists or safe extension checks
- return `404` or equivalent safe responses for sensitive or unknown backend paths
- avoid leaking filesystem layout or backend deployment details

## Verification

For changes in this area, verify:

- source editing still uses `source/`
- CLI pull still prefers `source/`
- fork/remix never copies `live/backend/`
- sensitive extensions and backend paths are not publicly served
- source archives exclude generated output, dependencies, caches, secrets, logs, and local-only files

Read:

```txt
.agent/harness/verification.md
```
