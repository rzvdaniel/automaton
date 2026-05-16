# Automaton Starter Prompts

Use these as starter prompts for Codex, Claude Code, Copilot Chat, Cursor, and similar AI tools.

Prefer the local generated Automaton guidance in the current folder after `automaton setup`. Run `automaton setup` inside the app folder you want to create or manage. Each app should have its own `automaton.json` and local guidance scaffold. Use `https://automaton.monster/llms.txt` only as the fallback when local guidance is not available yet.

## Official generic starter prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as a frontend-only Automaton app for: [describe the app]. Prefer plain HTML/CSS/JS with root-level files unless I ask for a framework. Use the Automaton CLI as the only execution path: start with automaton setup, create or link automaton.json with automaton new or automaton link, run automaton validate, deploy, and ask before publish. Only ask me to step in for authentication or publish approval.
```

## Existing app link prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. I already have an Automaton app. Use the Automaton CLI as the only execution path: start with automaton setup, link this folder to app id [app-id] with automaton link, scaffold or update the app locally, run automaton validate, deploy, and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

## Frontend-only prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as a frontend-only Automaton app for: [describe the app]. Prefer plain HTML, CSS, and JavaScript with root-level files unless the user explicitly asks for a framework or split source structure. Do not default to `frontend/` for plain apps. Use the Automaton CLI as the only execution path: start with automaton setup, create or link automaton.json with automaton new or automaton link, run automaton validate, deploy, and ask before publish. Only ask me to step in for authentication or publish approval.
```

## React prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as an Automaton app using React. Keep the React source in the repo. If the app is frontend-only, build to dist/ and set automaton.json so automaton deploy uploads dist. If the app also has Python backend controllers in backend/, do not assume deployDir: dist is enough unless backend/ is copied into dist/backend/. Instead, prepare one deploy-ready folder that contains both the built frontend files and backend/, then set deployDir to that folder. Use the Automaton CLI as the only execution path: start with automaton setup, create or link automaton.json with automaton new or automaton link, scaffold the app, run automaton validate, handle runner assignment only when backend controllers exist, deploy, and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```

## Vue prompt

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build this as an Automaton app using Vue. Keep the Vue source in the repo. If the app is frontend-only, build to dist/ and set automaton.json so automaton deploy uploads dist. If the app also has Python backend controllers in backend/, do not assume deployDir: dist is enough unless backend/ is copied into dist/backend/. Instead, prepare one deploy-ready folder that contains both the built frontend files and backend/, then set deployDir to that folder. Use the Automaton CLI as the only execution path: start with automaton setup, create or link automaton.json with automaton new or automaton link, scaffold the app, run automaton validate, handle runner assignment only when backend controllers exist, deploy, and ask before publish. Only ask me to step in for authentication, runner creation outside the CLI, or publish approval.
```
