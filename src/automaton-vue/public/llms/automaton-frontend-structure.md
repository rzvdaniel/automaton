# Automaton Frontend Structure

Automaton supports multiple frontend styles.

## Plain HTML/CSS/JS

Use this when the user wants the simplest structure.

```text
my-app/
|-- automaton.json
|-- frontend/
|   |-- index.html
|   |-- styles.css
|   `-- app.js
`-- backend/
```

Rules:
- Put plain static frontend files in `frontend/`.
- Use `frontend/index.html` as the entry point.
- Assets and additional pages can live under `frontend/`.

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
- Build to `dist/` or another static output folder.
- Put `build` and `deployDir` in `automaton.json` when needed.

Example:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "build": "npm run build",
  "deployDir": "dist"
}
```

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
