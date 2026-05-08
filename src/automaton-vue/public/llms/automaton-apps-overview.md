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
- Deploy with `automaton deploy`.
- If the app has Python backend controllers, inspect the assignment with `automaton current-runner`, list available runners with `automaton runners`, assign an existing connected runner with `automaton set-runner <runner-id>`, or tell the user to create or deploy one in Automaton Studio, then deploy.
- The CLI should be the primary autonomous path. MCP is a secondary enhancement when supported, not a requirement.

Recommended workflow for most developers:
1. Authenticate with `automaton login` if needed.
2. Create or link the app.
3. Run `automaton validate` before deploy.
4. Keep the project in the local repo.
5. Build with the user's preferred editor or AI tool.
6. Make sure `automaton.json` includes `build` and `deployDir` when the deployable frontend is not the project root.
7. For backend apps, ensure a runner exists and is assigned.
8. Deploy with `automaton deploy`.
9. Publish only after user approval.

Canonical autonomous order:

```bash
automaton whoami
automaton create --name "My App"
automaton validate
automaton current-runner
automaton runners
automaton set-runner <runner-id>
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
