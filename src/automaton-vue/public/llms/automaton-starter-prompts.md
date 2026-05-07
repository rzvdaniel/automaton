# Automaton Starter Prompts

## Generic prompt

```text
Read https://automaton.monster/llms.txt, then build this as an Automaton app for: [describe the app]. Choose the simplest suitable frontend stack unless the user explicitly asks for one: plain HTML/CSS/JS first, Vite + vanilla JS when tooling or modularity is useful, React or Vue only when clearly justified. If the stack choice is genuinely ambiguous, ask one short question before scaffolding. Use the Automaton CLI as the primary execution path. Before considering the app ready, authenticate with automaton login if needed, create or link automaton.json in the project root, run automaton validate, ensure a runner exists for backend controllers with automaton current-runner, automaton runners, or automaton ensure-local-runner --assign, then deploy. Only publish after explicit user confirmation.
```

## Plain HTML/CSS/JS prompt

```text
Build this as an Automaton app using plain HTML, CSS, and JavaScript with root-level frontend files by default. Keep index.html in the project root unless the user explicitly wants a separate frontend/ source folder. Only create backend/ when the app actually needs Python backend controllers. Each backend controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root. If backend/ exists, run automaton current-runner or automaton runners, prefer automaton ensure-local-runner --assign when no runner exists and Docker is available, run automaton validate, then deploy with automaton deploy.
```

## React prompt

```text
Build this as an Automaton app using React. Keep the React source code in the repo and build to dist/. If automaton.json already exists, add missing build and deployDir fields so automaton deploy runs the build and uploads dist. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root. If backend/ exists, run automaton current-runner or automaton runners, prefer automaton ensure-local-runner --assign when no runner exists and Docker is available, run automaton validate, then deploy with automaton deploy.
```

## Vue prompt

```text
Build this as an Automaton app using Vue. Keep the Vue source code in the repo and build to dist/. If automaton.json already exists, add missing build and deployDir fields so automaton deploy runs the build and uploads dist. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root. If backend/ exists, run automaton current-runner or automaton runners, prefer automaton ensure-local-runner --assign when no runner exists and Docker is available, run automaton validate, then deploy with automaton deploy.
```
