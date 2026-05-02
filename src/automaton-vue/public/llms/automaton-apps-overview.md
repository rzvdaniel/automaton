# Automaton Apps Overview

Automaton apps are frontend-agnostic static web apps with Python backends.

Use these rules:
- Keep `automaton.json` at the project root and commit it to Git.
- Frontend code can be plain HTML/CSS/JS or any framework that outputs static files.
- Python backend code lives in `backend/`.
- Deploy with `automaton deploy`.
- MCP is recommended when supported, but not required.

Recommended workflow for most developers:
1. Create or link the app.
2. Keep the project in the local repo.
3. Build with the user's preferred editor or AI tool.
4. Deploy with `automaton deploy`.

Use the linked files in this folder for exact backend, frontend, and starter-prompt rules.
