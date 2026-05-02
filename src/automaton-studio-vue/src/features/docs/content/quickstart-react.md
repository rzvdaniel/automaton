# Quick Start: React with dist output

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

## Recommended automaton.json

```json
{
  "appId": "your-app-id",
  "name": "My React App",
  "build": "npm run build",
  "deployDir": "dist"
}
```

## Rules

- Keep your React source code in the repo.
- Build to `dist/`.
- Deploy the built output with `automaton deploy`.
- Keep Python backend controllers in `backend/`.

## Starter prompt

```text
Build this as an Automaton app using React. Keep the React source code in the repo and build to dist/. Set automaton.json to run the build command and deploy from dist. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root and deploy with automaton deploy.
```