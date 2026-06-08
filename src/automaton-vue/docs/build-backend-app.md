---
title: Build a Backend App
description: Build a Python backend app with Automaton. Deploy controllers to a self-hosted Runner and call them from your frontend.
---

# Build a Backend App

Backend apps run on a Runner.

Use this page when your Automaton app needs APIs, server-side logic, database calls, scheduled work, or integrations that should not run in the browser.

## Create or link an app

```bash
automaton login
automaton new "My Backend App"
```

This creates the Automaton app and writes the local `automaton.json` file.

If remote app creation fails, keep the generated local scaffold and continue building in the same folder. Link it later with `automaton create` or `automaton link <app-id>`.

## Backend folder

Keep backend code in:

```txt
backend/
```

Typical structure:

```txt
my-app/
  automaton.json
  .automatonignore
  frontend/
  backend/
    main.py
```

## Backend contract

Automaton backend apps use Python controllers in `backend/`.

Do not create FastAPI, Flask, Express, aiohttp, uvicorn entrypoints, or custom HTTP listeners.

Each controller file must export `handle(action_name, inputs)` and return the standard envelope `{ success, data, error }`.

Example:

```python
class TodosController:
    def list(self, inputs: dict) -> dict:
        return {
            "success": True,
            "data": {"items": [{"id": 1, "title": "Ship app", "done": False}]},
            "error": ""
        }

    def create(self, inputs: dict) -> dict:
        title = str(inputs.get("title", "")).strip()
        if not title:
            return {"success": False, "data": None, "error": "title is required"}

        return {
            "success": True,
            "data": {"id": 2, "title": title, "done": False},
            "error": ""
        }

_controller = TodosController()

def handle(action_name: str, inputs: dict) -> dict:
    method = getattr(_controller, action_name, None)
    if method is None:
        return {"success": False, "data": None, "error": f"Unknown action: {action_name}"}

    return method(inputs)
```

Your frontend should call backend actions with `window.automatonApi.callController(...)`:

```js
try {
  const data = await window.automatonApi.callController('todos', 'list', {})
  console.log(data.items)
} catch (err) {
  console.error(err.message)
}
```

## Configure backend deploy

In `automaton.json`, point Automaton to the backend folder:

```json
{
  "appId": "your-app-id",
  "name": "My Backend App",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
```

If the local scaffold exists but is not linked yet, the same config shape is valid without `appId` until you run `automaton create` or `automaton link`.

## Deploy

```bash
automaton deploy
```

Deploy uploads the frontend and backend runtime.

If a Runner is already connected, backend APIs can become active during deploy.

If no Runner is connected yet, deploy can still upload the backend, but the backend will not run until you connect a Runner.

## Next step

Install a Runner before publishing backend apps you want people to use.

For a fuller controller contract and starter examples, see:

- [Automaton backend controllers](https://automaton.monster/llms/automaton-backend-controllers.md)
- [Automaton examples](https://automaton.monster/llms/automaton-examples.md)
