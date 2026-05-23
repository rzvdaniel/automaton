# Automaton Apps Overview

Automaton apps are frontend-agnostic static web apps with Python backends.

Use these rules:
- Keep `automaton.json` at the project root and commit it to Git.
- Treat the local project as source. Frontend source normally lives in `frontend/`; backend source lives in `backend/`.
- `automaton deploy` uploads `frontend.deployDir` into `runtime/frontend/` and `backend.deployDir` into `runtime/backend/` when present.
- Only `runtime/frontend/` is public. Source files and backend runtime files are never publicly served.
- If `automaton.json` already exists and the app uses a build step, add missing `frontend.build` and `frontend.deployDir` fields instead of leaving deploy behavior implicit.
- If the app is build-based and also has Python backend controllers, keep backend source in `backend/`; do not copy it into frontend `deployDir`.
- Frontend code for AI-generated local apps should default to Vite vanilla TypeScript or JavaScript under `frontend/`.
- Python backend code lives in `backend/`.
- Default to frontend-only. Create `backend/` only when the user-requested core feature requires server-side execution, secrets, persistence, auth, scheduled work, or browser-unsafe access that cannot be removed without breaking the requested app.
- Do not create `backend/` only for convenience, caching, minor proxying, CORS or mixed-content workarounds for non-essential data, or optional enrichment.
- When backend controllers are required, implement Automaton Python controllers that export `handle(action_name, inputs)`. Do not create a standalone FastAPI, Flask, or other web server under `backend/`.
- Do not create empty `backend/` or `data/` folders for frontend-only apps.
- For AI-generated local apps, default to Vite + vanilla TypeScript or JavaScript in `frontend/`.
- AI-generated local apps should always use a frontend build step and deploy only `frontend/dist/` as public runtime.
- Do not move Vite output to root `dist/` unless the user explicitly requested a no-build static app or Automaton CLI output proves that `frontend/dist/` cannot work.
- Root-level frontend files are an explicit exception only for no-build static apps requested by the user; set `"frontend": { "deployDir": "." }` in that mode.
- Deploy with `automaton deploy`.
- If the app has Python backend controllers, let `automaton deploy` auto-select or prompt for a connected runner. Tell the user to create or deploy one in Automaton Studio only when no connected runner exists.
- `automaton setup`, `automaton new`, and `automaton link` automatically sync a local AI guidance pack; use `automaton sync-ai` only when those generated files need a manual refresh.

Recommended workflow for most developers:
1. Run `automaton setup`.
2. Run `automaton new "My App"` for a new app, or `automaton link <app-id>` for an existing one.
3. Run `automaton sync-ai` only if the generated local guidance files are missing or stale.
4. Run `automaton deploy`; it performs the deploy preflight.
5. Keep the project in the local repo.
6. Build with the user's preferred editor or AI tool.
7. Make sure `automaton.json` includes `frontend.build` and `frontend.deployDir` when the deployable frontend is not the project root.
8. For backend apps, let deploy handle runner selection when a connected runner exists.
9. Use `automaton validate` only as an optional diagnostic check.
10. Publish only after user approval.

Do not consider local build, type-check, tests, or `python -m py_compile` to be completion. Before declaring the app done, run `automaton deploy`; if it fails, report the exact command, exact error, and the next step.

Canonical autonomous order:

```bash
automaton setup
automaton new "My App"
automaton deploy
# ask user before publish
automaton publish --yes
automaton status
```

Minimal `automaton.json`:

```json
{
	"appId": "your-app-id",
	"name": "My App",
	"server": "https://api.automaton.monster"
}
```

Frontend-only build-based `automaton.json`:

```json
{
		"appId": "your-app-id",
		"name": "My App",
		"server": "https://api.automaton.monster",
		"frontend": {
			"build": "cd frontend && npm install && npm run build",
			"deployDir": "frontend/dist"
		}
}
```

Frontend-plus-backend build-based `automaton.json`:

```json
{
		"appId": "your-app-id",
		"name": "My App",
		"server": "https://api.automaton.monster",
		"frontend": {
			"build": "cd frontend && npm install && npm run build",
			"deployDir": "frontend/dist"
		},
	"backend": {
		"deployDir": "backend"
	}
}
```

Use the linked files in this folder for exact backend, frontend, and starter-prompt rules.
