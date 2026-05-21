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
- Do not create empty `backend/` or `data/` folders for frontend-only apps.
- For AI-generated local apps, default to Vite + vanilla TypeScript or JavaScript in `frontend/`.
- AI-generated local apps should always use a frontend build step and deploy only `frontend/dist/` as public runtime.
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

Canonical autonomous order:

```bash
automaton setup
automaton new "My App"
automaton deploy
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
