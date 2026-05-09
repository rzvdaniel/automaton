# Automaton Frontend Structure

Automaton supports multiple frontend styles.

## How to choose a stack

Use the simplest stack that fits the project.

### Prefer plain HTML/CSS/JS when

- the app is mostly one page
- the state is local or simple
- the user did not ask for a framework
- the app is a lightweight internal tool, planning app, utility, dashboard, or form

### Prefer Vite + vanilla HTML/CSS/JS when

- the app is still framework-free
- but benefits from ES modules, a dev server, build tooling, or cleaner source organization
- there are multiple frontend modules or a modest amount of interactive UI logic

### Prefer React or Vue when

- the user explicitly asks for React or Vue
- the app clearly needs component-heavy UI composition, SPA routing, or a richer frontend architecture
- the project already implies that stack

### Ask the user when

- more than one stack is equally reasonable
- the prompt does not imply complexity level
- the choice would materially change the resulting project structure

## Plain HTML/CSS/JS

Use this when the user wants the simplest structure.

```text
my-app/
|-- automaton.json
|-- index.html
|-- styles.css
|-- app.js
`-- backend/
```

Rules:
- Prefer putting plain static frontend files at the project root.
- Use `index.html` at the project root as the entry point.
- Assets and additional pages can live in root-level folders like `js/`, `css/`, `assets/`, or `pages/`.
- Do not create empty `backend/` or `data/` folders for frontend-only apps.

If the user explicitly wants a separate frontend source folder, this is acceptable:

```text
my-app/
|-- automaton.json
|-- frontend/
|   |-- index.html
|   |-- styles.css
|   `-- app.js
`-- backend/
```

In that case:
- only use `frontend/` when the user asked for a split source structure or there is a clear organizational reason
- still keep `automaton.json` in the project root
- do not create empty backend folders unless the app actually uses backend controllers

## Vite + vanilla HTML/CSS/JS

Use this when the app should stay framework-free but still wants modern tooling.

```text
my-app/
|-- automaton.json
|-- package.json
|-- index.html
|-- src/
|-- public/          (optional)
|-- dist/
`-- backend/
```

Rules:
- Keep the app framework-free unless the user asked for React or Vue.
- Use Vite for modules, development workflow, and build output.
- Build to `dist/` and set `build` and `deployDir` in `automaton.json`.
- Do not create empty `backend/` or `data/` folders for frontend-only apps.

Example:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "build": "npm run build",
  "deployDir": "dist"
}
```

## React, Vue, Angular, or other framework apps

Use this when the user wants a full local framework project.

```text
my-app/
|-- automaton.json
|-- package.json
|-- src/
|-- public/
|-- dist/
`-- backend/
```

Rules:
- Keep framework source code in the repo.
- For frontend-only apps, build to `dist/` or another static output folder.
- For apps with Python backend controllers, do not assume `dist/` alone is deployable unless `backend/` is copied into that deploy directory.
- Put `build` and `deployDir` in `automaton.json` when needed.
- For build-based frontend-plus-backend apps, prepare one deploy-ready folder that contains both the built frontend files and `backend/`, then point `deployDir` at that folder.
- Do not create empty `backend/` or `data/` folders for frontend-only apps.

Frontend-only example:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "build": "npm run build",
  "deployDir": "dist"
}
```

Frontend-plus-backend example:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "build": "npm run build && npm run prepare-deploy",
  "deployDir": "deploy"
}
```

If `automaton.json` already exists, add missing `build` and `deployDir` fields instead of assuming deploy from the project root.

## Frontend to backend calls

Frontend code must call backend actions with:

```javascript
await window.automatonApi.callController(controller, action, inputs)
```

Important:
- `callController()` returns the backend `data` payload directly.
- It throws on error.
- Always use `try/catch`.

Example:

```javascript
try {
  const data = await window.automatonApi.callController('todos', 'list', { page: 1 })
  console.log(data.items)
} catch (err) {
  console.error(err.message)
}
```
