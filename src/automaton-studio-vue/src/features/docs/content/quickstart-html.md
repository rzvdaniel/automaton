# Quick Start: HTML, CSS, and JavaScript

Use this path when you want the simplest possible project structure.

## Structure

```text
my-app/
├── automaton.json
├── frontend/
│   ├── index.html
│   ├── styles.css
│   └── app.js
└── backend/
    ├── todos.py
    ├── shared/
    └── mocks/
```

## Rules

- Put the frontend in `frontend/`.
- Use `frontend/index.html` as the entry point.
- Put Python controllers in `backend/`.
- Put shared helpers in `backend/shared/`.
- Put local mock responses in `backend/mocks/{controller}.{action}.json`.

## Starter prompt

```text
Build this as an Automaton app using plain HTML, CSS, and JavaScript in frontend/. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root and deploy with automaton deploy.
```

## Deploy

```bash
automaton deploy
```