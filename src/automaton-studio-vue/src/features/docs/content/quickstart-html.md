# Quick Start: HTML, CSS, and JavaScript

Use this path when you want the simplest possible project structure.

## Structure

```text
my-app/
├── automaton.json
├── index.html
├── styles.css
├── js/
└── backend/
    ├── todos.py
    ├── shared/
    └── mocks/
```

## Rules

- Prefer root-level frontend files for plain HTML apps.
- Use `index.html` in the project root as the entry point.
- Put additional frontend files in root-level folders like `js/`, `css/`, `assets/`, or `pages/`.
- Put Python controllers in `backend/`.
- Put shared helpers in `backend/shared/`.
- Put local mock responses in `backend/mocks/{controller}.{action}.json`.
- A separate `frontend/` source folder is optional, not the default.

## Starter prompt

```text
Build this as an Automaton app using plain HTML, CSS, and JavaScript with root-level frontend files by default. Keep index.html in the project root unless the user explicitly wants a separate frontend/ source folder. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root and deploy with automaton deploy.
```

## Deploy

```bash
automaton deploy
```