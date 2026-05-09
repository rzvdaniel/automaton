# Automaton Starter Prompts

Use these as starter prompts for Codex, Claude Code, Copilot Chat, Cursor, and similar AI tools.

Prefer the local generated Automaton guidance in the current folder after `automaton setup`. Run `automaton setup` inside the app folder you want to create or manage. Each app should have its own `automaton.json` and local guidance scaffold. Use `https://automaton.monster/llms.txt` only as the fallback when local guidance is not available yet.

## Official generic starter prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as an Automaton app for: [describe the app]. Choose the simplest suitable frontend stack unless the user explicitly asks for one: plain HTML/CSS/JS first, Vite + vanilla JS when tooling or modularity is useful, React or Vue only when clearly justified. For plain HTML/CSS/JS apps, keep `index.html`, CSS, and JS at the project root by default instead of creating `frontend/` unless I explicitly ask for a split source structure. If the stack choice is genuinely ambiguous, ask one short question before scaffolding. Use the Automaton CLI as the primary execution path. Start with automaton setup. After that, handle the rest yourself: install the CLI if needed, create or link automaton.json in this folder, scaffold the app, run automaton validate, and if backend controllers exist, pick an existing connected runner with automaton current-runner, automaton runners, and automaton set-runner <runner-id>, or tell me to create or deploy one in Automaton Studio. Then deploy, and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

## Existing app link prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. I already have an Automaton app. Use the Automaton CLI as the primary execution path. Start with automaton setup. After that, handle the rest yourself: link this folder to app id [app-id], scaffold or update the app locally, keep automaton.json correct in this folder, run automaton validate, deploy, and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

## Frontend-only prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as a frontend-only Automaton app for: [describe the app]. Prefer plain HTML, CSS, and JavaScript with root-level frontend files unless the user explicitly asks for a framework or split source structure. Do not default to `frontend/` for plain apps. Only create backend/ if the app truly needs Python backend controllers. Use the Automaton CLI as the primary execution path. Start with automaton setup. After that, handle the rest yourself: install the CLI if needed, create or link automaton.json in this folder, scaffold the app, run automaton validate, deploy, and ask before publish. Only ask me to step in for authentication or publish approval.
```

## React prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as an Automaton app using React. Keep the React source in the repo. If the app is frontend-only, build to dist/ and set automaton.json so automaton deploy uploads dist. If the app also has Python backend controllers in backend/, do not assume deployDir: dist is enough unless backend/ is copied into dist/backend/. Instead, prepare one deploy-ready folder that contains both the built frontend files and backend/, then set deployDir to that folder. Use the Automaton CLI as the primary execution path. Start with automaton setup. After that, handle the rest yourself: install the CLI if needed, create or link automaton.json in this folder, scaffold the app, run automaton validate, handle runner assignment only when backend controllers exist, deploy, and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

## Vue prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as an Automaton app using Vue. Keep the Vue source in the repo. If the app is frontend-only, build to dist/ and set automaton.json so automaton deploy uploads dist. If the app also has Python backend controllers in backend/, do not assume deployDir: dist is enough unless backend/ is copied into dist/backend/. Instead, prepare one deploy-ready folder that contains both the built frontend files and backend/, then set deployDir to that folder. Use the Automaton CLI as the primary execution path. Start with automaton setup. After that, handle the rest yourself: install the CLI if needed, create or link automaton.json in this folder, scaffold the app, run automaton validate, handle runner assignment only when backend controllers exist, deploy, and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```
