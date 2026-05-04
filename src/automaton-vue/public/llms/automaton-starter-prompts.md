# Automaton Starter Prompts

## Generic prompt

```text
Read https://automaton.monster/llms.txt, then build this as an Automaton app for: [describe the app]. Use the Automaton CLI to deploy it.
```

## Plain HTML/CSS/JS prompt

```text
Build this as an Automaton app using plain HTML, CSS, and JavaScript in frontend/. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root and deploy with automaton deploy.
```

## React prompt

```text
Build this as an Automaton app using React. Keep the React source code in the repo and build to dist/. Set automaton.json to run the build command and deploy from dist. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root and deploy with automaton deploy.
```

## Vue prompt

```text
Build this as an Automaton app using Vue. Keep the Vue source code in the repo and build to dist/. Set automaton.json to run the build command and deploy from dist. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Keep automaton.json in the project root and deploy with automaton deploy.
```
