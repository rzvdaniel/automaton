# Automaton Apps Overview

Automaton apps are frontend-agnostic static web apps with Python backends.

Use these rules:
- Keep `automaton.json` at the project root and commit it to Git.
- If `automaton.json` already exists and the app uses a build step, add missing `build` and `deployDir` fields instead of leaving deploy behavior implicit.
- Frontend code can be plain HTML/CSS/JS or any framework that outputs static files.
- Python backend code lives in `backend/`.
- Do not create empty `backend/` or `data/` folders for frontend-only apps.
- For plain HTML/CSS/JS apps, prefer root-level frontend files unless the user explicitly wants a separate `frontend/` source folder.
- Deploy with `automaton deploy`.
- MCP is recommended when supported, but not required.

Recommended workflow for most developers:
1. Create or link the app.
2. Keep the project in the local repo.
3. Build with the user's preferred editor or AI tool.
4. Make sure `automaton.json` includes `build` and `deployDir` when the deployable frontend is not the project root.
5. Deploy with `automaton deploy`.

Minimal `automaton.json`:

```json
{
	"appId": "your-app-id",
	"name": "My App",
	"server": "https://api.automaton.monster"
}
```

Build-based `automaton.json`:

```json
{
	"appId": "your-app-id",
	"name": "My App",
	"server": "https://api.automaton.monster",
	"build": "npm run build",
	"deployDir": "dist"
}
```

Use the linked files in this folder for exact backend, frontend, and starter-prompt rules.
