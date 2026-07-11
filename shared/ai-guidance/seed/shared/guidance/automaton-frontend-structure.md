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
- Do not add Automaton runtime script tags such as `automaton-config.js` or `automaton-api.js` to app source; the platform injects those during publish.
- Keep service worker files and registrations inside the same relative app scope. Avoid root-relative worker URLs.
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

### Canonical installable PWA starter

Use this pattern when the app should be installable on mobile/desktop.

`frontend/index.html`

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="theme-color" content="#0f172a" />
    <link rel="manifest" href="./manifest.webmanifest" />
    <link rel="icon" href="./icons/icon-192.png" sizes="192x192" type="image/png" />
    <link rel="apple-touch-icon" href="./icons/icon-192.png" />
    <title>My App</title>
  </head>
  <body>
    <main>
      <h1>My App</h1>
      <button id="installButton" type="button" hidden>Install app</button>
    </main>
    <script type="module" src="./src/main.ts"></script>
  </body>
</html>
```

`frontend/public/manifest.webmanifest`

```json
{
  "name": "My App",
  "short_name": "My App",
  "id": "./",
  "start_url": "./",
  "scope": "./",
  "display": "standalone",
  "background_color": "#0f172a",
  "theme_color": "#0f172a",
  "icons": [
    {
      "src": "./icons/icon-192.png",
      "sizes": "192x192",
      "type": "image/png",
      "purpose": "any maskable"
    },
    {
      "src": "./icons/icon-512.png",
      "sizes": "512x512",
      "type": "image/png",
      "purpose": "any maskable"
    }
  ]
}
```

`frontend/src/main.ts`

```ts
let deferredInstallPrompt: Event | null = null

const installButton = document.querySelector<HTMLButtonElement>('#installButton')

window.addEventListener('beforeinstallprompt', (event) => {
  event.preventDefault()
  deferredInstallPrompt = event
  installButton?.removeAttribute('hidden')
})

installButton?.addEventListener('click', async () => {
  if (!deferredInstallPrompt) {
    return
  }

  const promptEvent = deferredInstallPrompt as Event & {
    prompt: () => Promise<void>
    userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
  }

  await promptEvent.prompt()
  await promptEvent.userChoice
  deferredInstallPrompt = null
  installButton.hidden = true
})

window.addEventListener('appinstalled', () => {
  deferredInstallPrompt = null
  if (installButton) {
    installButton.hidden = true
  }
})

if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    void navigator.serviceWorker.register('./sw.js')
  })
}
```

`frontend/public/sw.js`

```js
self.addEventListener('install', () => {
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim())
})
```

Rules:
- Keep manifest, icon, and service worker paths relative.
- Register the service worker with `./sw.js`, not `/sw.js`.
- Show the install button only after `beforeinstallprompt` fires.
- Do not add Automaton runtime scripts here. Publish injects them.
- Validate install from the published `apps.automaton.monster/{slug}/` URL, not from Studio preview.

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
- For apps with HTTP backends, keep backend source in `backend/`; frontend runtime deploy still points to the frontend build output.
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

Frontend code must use app-relative HTTP under `api/` so it works when hosted under `/a/{appId}/`:

```javascript
await fetch('api/posts')
```

Important:
- Use relative paths so deployed apps work across environments.
- Use normal HTTP methods, JSON bodies, and HTTP status codes.

Example:

```javascript
try {
  const response = await fetch('api/posts?page=1')
  if (!response.ok) throw new Error(`Request failed: ${response.status}`)
  const data = await response.json()
  console.log(data.items)
} catch (err) {
  console.error(err.message)
}
```
