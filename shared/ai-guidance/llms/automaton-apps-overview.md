# Automaton Apps Overview

Automaton apps are frontend-agnostic static web apps with Python backends.

Use these rules:
- Keep `automaton.json` at the project root and commit it to Git.
- If `automaton.json` already exists and the app uses a build step, add missing `build` and `deployDir` fields instead of leaving deploy behavior implicit.
- If the app is build-based and also has Python backend controllers, make `deployDir` point to a deploy-ready folder that contains both the built frontend files and `backend/`, or copy `backend/` into that deploy directory before deploy.
- Frontend code can be plain HTML/CSS/JS or any framework that outputs static files.
- Python backend code lives in `backend/`.
- Do not create empty `backend/` or `data/` folders for frontend-only apps.
- For plain HTML/CSS/JS apps, prefer root-level frontend files unless the user explicitly wants a separate `frontend/` source folder.
- Do not default plain apps to `frontend/` just because it is a valid deploy directory.
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
7. Make sure `automaton.json` includes `build` and `deployDir` when the deployable frontend is not the project root.
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
	"build": "npm run build",
	"deployDir": "dist"
}
```

Frontend-plus-backend build-based `automaton.json`:

```json
{
	"appId": "your-app-id",
	"name": "My App",
	"server": "https://api.automaton.monster",
	"build": "npm run build && npm run prepare-deploy",
	"deployDir": "deploy"
}
```

Use the linked files in this folder for exact backend, frontend, and starter-prompt rules.
