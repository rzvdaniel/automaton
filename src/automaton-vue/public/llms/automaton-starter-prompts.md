# Automaton Starter Prompts

## Generic prompt

```text
Read https://automaton.monster/llms.txt, then build this as an Automaton app for: [describe the app]. Choose the simplest suitable frontend stack unless the user explicitly asks for one: plain HTML/CSS/JS first, Vite + vanilla JS when tooling or modularity is useful, React or Vue only when clearly justified. If the stack choice is genuinely ambiguous, ask one short question before scaffolding. Use the Automaton CLI to deploy it. Before considering the app ready, create or link automaton.json in the project root.
```

## Plain HTML/CSS/JS prompt

```text
Build this as an Automaton app using plain HTML, CSS, and JavaScript with root-level frontend files by default. Keep index.html in the project root unless the user explicitly wants a separate frontend/ source folder. Only create backend/ when the app actually needs Python backend controllers. Each backend controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root and deploy with automaton deploy.
```

## React prompt

```text
Build this as an Automaton app using React. Keep the React source code in the repo and build to dist/. If automaton.json already exists, add missing build and deployDir fields so automaton deploy runs the build and uploads dist. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root and deploy with automaton deploy.
```

## Vue prompt

```text
Build this as an Automaton app using Vue. Keep the Vue source code in the repo and build to dist/. If automaton.json already exists, add missing build and deployDir fields so automaton deploy runs the build and uploads dist. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root and deploy with automaton deploy.
```
