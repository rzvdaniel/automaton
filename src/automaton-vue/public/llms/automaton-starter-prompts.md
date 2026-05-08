# Automaton Starter Prompts

## Generic prompt

```text
Read https://automaton.monster/llms.txt, then build this as an Automaton app for: [describe the app]. Choose the simplest suitable frontend stack unless the user explicitly asks for one: plain HTML/CSS/JS first, Vite + vanilla JS when tooling or modularity is useful, React or Vue only when clearly justified. If the stack choice is genuinely ambiguous, ask one short question before scaffolding. Use the Automaton CLI as the primary execution path. Before considering the app ready, authenticate with automaton login if needed, create or link automaton.json in the project root, run automaton validate, and for backend controllers pick an existing connected runner with automaton current-runner, automaton runners, and automaton set-runner <runner-id>, or tell the user to create or deploy one in Automaton Studio, then deploy. Only publish after explicit user confirmation.
```

## Plain HTML/CSS/JS prompt

```text
Build this as an Automaton app using plain HTML, CSS, and JavaScript with root-level frontend files by default. Keep index.html in the project root unless the user explicitly wants a separate frontend/ source folder. Only create backend/ when the app actually needs Python backend controllers. Each backend controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root. If backend/ exists, run automaton current-runner or automaton runners, pick an existing connected runner with automaton set-runner <runner-id>, or tell the user to create or deploy one in Automaton Studio if none exists, run automaton validate, then deploy with automaton deploy.
```

## React prompt

```text
Build this as an Automaton app using React. Keep the React source code in the repo. If the app is frontend-only, build to dist/ and set automaton.json so automaton deploy uploads dist. If the app also has Python backend controllers in backend/, do not assume deployDir: dist is enough unless backend/ is copied into dist/backend/. Instead, prepare one deploy-ready folder that contains both the built frontend files and backend/, then set deployDir to that folder. Each backend controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root. If backend/ exists, run automaton current-runner or automaton runners, pick an existing connected runner with automaton set-runner <runner-id>, or tell the user to create or deploy one in Automaton Studio if none exists, run automaton validate, then deploy with automaton deploy.
```

## Vue prompt

```text
Build this as an Automaton app using Vue. Keep the Vue source code in the repo. If the app is frontend-only, build to dist/ and set automaton.json so automaton deploy uploads dist. If the app also has Python backend controllers in backend/, do not assume deployDir: dist is enough unless backend/ is copied into dist/backend/. Instead, prepare one deploy-ready folder that contains both the built frontend files and backend/, then set deployDir to that folder. Each backend controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root. If backend/ exists, run automaton current-runner or automaton runners, pick an existing connected runner with automaton set-runner <runner-id>, or tell the user to create or deploy one in Automaton Studio if none exists, run automaton validate, then deploy with automaton deploy.
```
