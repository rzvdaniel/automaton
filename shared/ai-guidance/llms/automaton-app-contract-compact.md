# Automaton App Contract (Compact)

Use this compact contract for in-product app editing where linked documents are not automatically available as prompt context.

## Required app shape

- Keep `automaton.json` in the app root when it exists.
- In Studio, file tools edit the app source surfaces rather than the local CLI scaffold directly.
- Studio-backed app source is organized around frontend and backend source roots.
- Frontend source normally lives in `frontend/`.
- Frontend build output normally lives in `frontend/dist/`.
- Backend source, when needed, lives in `backend/`.
- Treat frontend and backend as separate surfaces.
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
```

## Frontend rules

- Use relative frontend API URLs so deployed apps keep working across environments.
- Do not hardcode localhost-only frontend URLs into the shipped app.
- Prefer small, focused file changes when updating an existing app.

## Backend rules

- Automaton backends are Python controller-based backends.
- Do not introduce FastAPI, Flask, Express, ASP.NET, or custom long-running servers unless the user explicitly asks for a non-standard setup.
- Preserve the controller response envelope expected by `window.automatonApi.callController(...)`.
- Backend controller responses should use the standard shape:

```json
{ "success": true, "data": null, "error": null }
```

- On errors, return the same envelope shape with `success: false` and a useful `error` message.
- Keep backend files under `backend/`.

## Runtime data

- Durable per-app runtime data belongs under `AUTOMATON_APP_DATA_DIR`.
- Do not hardcode the physical storage path.
- Use subfolders like `media/`, `db/`, `generated/`, `cache/`, `logs/`, and `tmp/` when they fit.
- Only create folders that the app actually needs.

## Publishing and deployment

- Deploy and publish are separate actions.
- Ask before publishing an app publicly.
- When backend files change, remind the user that backend/runtime changes must be deployed before they take effect.
