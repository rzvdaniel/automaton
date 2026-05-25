# Automaton Frontend Structure

Automaton supports multiple frontend styles.

## How to choose a stack

Use the simplest stack that fits the project, but for AI-generated local apps the default must still be a build-based frontend that outputs to `frontend/dist/`.

### Prefer Vite + vanilla TypeScript when

- the app is AI-generated and the user did not ask for another stack
- the app will be iterated and redeployed frequently
- cache-friendly hashed CSS/JS assets matter
- the app benefits from modules, a dev server, build tooling, or cleaner source organization

### Prefer plain HTML/CSS/JS when

- the app is mostly one page
- the state is local or simple
- the app is a very small static demo
- the user explicitly asks for the simplest no-build structure

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

Use this only when the user explicitly wants a no-build static structure.

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
- Do not use this as the default AI-generated local app structure.
- Use it only for an explicit no-build static request.
- Do not create empty `backend/` or `data/` folders for frontend-only apps.

Root static deployment is acceptable only for an explicit no-build static request where the project root contains public files only:

```text
my-app/
|-- automaton.json
|-- index.html
|-- styles.css
`-- app.js
```

In that case:
- set `"frontend": { "deployDir": "." }`
- do not include source-only, backend, secret, or dependency files at the root

## Vite + vanilla TypeScript or HTML/CSS/JS

Use this as the default for AI-generated Automaton apps unless the user asks for another framework.

```text
my-app/
|-- automaton.json
|-- frontend/
|   |-- package.json
|   |-- index.html
|   |-- src/
|   |-- public/          (optional)
|   `-- dist/
`-- backend/
```

Rules:
- Keep the app framework-free unless the user asked for React or Vue.
- Use Vite for modules, development workflow, and build output.
- Build to `frontend/dist/` and set `frontend.build` and `frontend.deployDir` in `automaton.json`.
- Set `base: './'` in `frontend/vite.config.ts` so the published build uses relative asset URLs under `apps.automaton.monster/{slug}/`.
- Do not hardcode root-relative app-local URLs such as `/assets/...`, `/manifest.webmanifest`, `/favicon.ico`, or `/`; publish validation rejects them.
- Do not move Vite output to root `dist/` unless the user explicitly requested a no-build static app or Automaton CLI output proves that `frontend/dist/` cannot work.
- Treat the build pipeline as required for AI-generated local apps.
- Do not put source files, `package.json`, or Vite config into runtime.
- Do not create empty `backend/` or `data/` folders for frontend-only apps.

Example:

```ts
// frontend/vite.config.ts
import { defineConfig } from 'vite'

export default defineConfig({
  base: './'
})
```

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

## React, Vue, Angular, or other framework apps

Use this when the user wants a full local framework project.

```text
my-app/
|-- automaton.json
|-- frontend/
|   |-- package.json
|   |-- src/
|   |-- public/
|   `-- dist/
`-- backend/
```

Rules:
- Keep framework source code in the repo.
- For frontend-only apps, build to `frontend/dist/` or another static output folder under `frontend/`.
- Do not move framework build output to root `dist/` unless the user explicitly requested a no-build static app or Automaton CLI output proves that the framework output under `frontend/` cannot work.
- For apps with Python backend controllers, keep backend source in `backend/`; frontend runtime deploy still points to the frontend build output.
- Put `frontend.build` and `frontend.deployDir` in `automaton.json` when needed.
- `automaton deploy` uploads frontend runtime and backend runtime separately.
- Do not create empty `backend/` or `data/` folders for frontend-only apps.

Frontend-only example:

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

Frontend-plus-backend example:

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

If `automaton.json` already exists, add missing `frontend.build` and `frontend.deployDir` fields instead of assuming deploy from the project root.

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
