---
title: Build a Backend App
description: Build a Python HTTP backend app with Automaton and call it from your frontend.
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

Automaton backend apps are ordinary HTTP applications. The initial supported Python convention is FastAPI/ASGI with `main:app`.

Example:

```python
from fastapi import FastAPI

app = FastAPI()

@app.get("/api/todos")
def list_todos():
    return {"items": [{"id": 1, "title": "Ship app", "done": False}]}

@app.post("/api/todos")
def create_todo(todo: dict):
    return {"todo": todo}
```

Your frontend should call app-relative HTTP endpoints under `api/`:

```js
try {
  const response = await fetch('api/todos')
  if (!response.ok) throw new Error(`Request failed: ${response.status}`)
  const data = await response.json()
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

For fuller backend guidance and starter examples, see:

- [Automaton HTTP backends](https://automaton.monster/llms/automaton-backend-controllers.md)
- [Automaton examples](https://automaton.monster/llms/automaton-examples.md)
