# Quick Start: React

Use this path when you want to build in React and deploy static output to Automaton.

## Structure

```text
my-app/
├── automaton.json
├── package.json
├── src/
├── public/
├── dist/
└── backend/
```

If the app has Python backend controllers, add a deploy-ready folder only when you need one, for example:

```text
my-app/
├── automaton.json
├── package.json
├── src/
├── public/
├── dist/
├── deploy/
│   ├── index.html
│   ├── assets/
│   └── backend/
└── backend/
```

## Recommended automaton.json

```json
{
  "appId": "your-app-id",
  "name": "My React App",
  "build": "npm run build",
  "deployDir": "dist"
}
```

For backend-enabled React apps, use a deploy-ready folder instead:

```json
{
  "appId": "your-app-id",
  "name": "My React App",
  "build": "npm run build && npm run prepare-deploy",
  "deployDir": "deploy"
}
```

## Rules

- Keep your React source code in the repo.
- For frontend-only apps, build to `dist/` and deploy that output.
- If the app has Python backend controllers, do not deploy `dist/` unless `backend/` is copied into `dist/backend/`.
- For frontend-plus-backend apps, prepare one deploy-ready folder that contains both the built frontend files and `backend/`, then deploy that folder.
- Keep Python backend controllers in `backend/`.

## Starter prompt

```text
Build this as an Automaton app using React. Keep the React source code in the repo. If the app is frontend-only, build to dist/ and set automaton.json to deploy dist. If the app also has Python backend controllers in backend/, do not assume dist alone is enough unless backend/ is copied into dist/backend/. Instead, prepare one deploy-ready folder that contains both the built frontend files and backend/, then set deployDir to that folder. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root and deploy with automaton deploy.
```