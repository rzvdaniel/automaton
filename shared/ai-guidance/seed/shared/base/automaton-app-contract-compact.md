# Automaton App Contract (Compact)

Use this compact contract for in-product app editing where linked documents are not automatically available as prompt context.

## Required app shape

- Keep `automaton.json` in the app root when it exists.
- In Studio, file tools edit the app source surfaces rather than the local CLI scaffold directly.
- Studio-backed app source is organized around optional frontend, backend, and Action module source roots.
- Frontend source normally lives in `frontend/`.
- Frontend build output normally lives in `frontend/dist/`.
- Backend source, when needed, lives in `backend/`.
- Finite background work lives in `actions/{action-key}/`.
- Treat frontend, backend, and Actions as separate execution surfaces owned by the same App.
- Do not invent alternative top-level folders unless the existing app already uses them.

Minimum expected source shape:

```text
frontend/
  package.json
  src/
  public/
  dist/

backend/
  *.py

actions/
  send-email/
    action.yaml
    main.py
```

## Frontend rules

- Use relative frontend API URLs so deployed apps keep working across environments.
- Do not hardcode localhost-only frontend URLs into the shipped app.
- Prefer small, focused file changes when updating an existing app.

## Backend rules

- Automaton backends are ordinary HTTP applications. The initial supported convention is FastAPI/ASGI with `main:app`.
- Frontends call same-origin root-relative HTTP paths such as `/api/posts`.
- Published apps run on an app-specific origin derived from the slug; do not hardcode app IDs, hostnames, or ports.
- Keep the backend server behind the runner gateway. The browser should not know that it is running on Automaton.
- Use normal HTTP resources and methods, for example `GET /api/posts` and `POST /api/posts`.
- Backend responses should use the application’s normal JSON shape.

```json
{ "posts": [] }
```

- Return appropriate HTTP status codes and useful JSON error messages.
- Keep backend files under `backend/`.

## Runtime data

- Durable per-app runtime data belongs under `AUTOMATON_APP_DATA_DIR`.
- Do not hardcode the physical storage path.
- Use subfolders like `media/`, `db/`, `generated/`, `cache/`, `logs/`, and `tmp/` when they fit.
- Only create folders that the app actually needs.

## Action rules

- Each direct child of `actions/` is one Action module and must contain `action.yaml`.
- Supported runtimes are Python and Bash. Actions are console jobs, not HTTP servers.
- Read exactly one JSON value from stdin, write exactly one JSON value to stdout, and write logs to stderr.
- Exit non-zero on failure. Receive secrets through environment variables; never write them to output or logs.
- Normal App Actions cannot request host or Docker privileges.

## Publishing and deployment

- Deploy and publish are separate actions.
- Ask before publishing an app publicly.
- When backend files change, remind the user that backend/runtime changes must be deployed before they take effect.
