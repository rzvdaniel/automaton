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

## Backend folder

Keep backend code in:

```txt
backend/
```

Typical structure:

```txt
my-app/
  automaton.json
  frontend/
  backend/
    main.py
```

## Simple Python backend

Automaton backend apps use Python. A simple FastAPI backend can look like this:

```python
from fastapi import FastAPI

app = FastAPI()

@app.get("/api/hello")
def hello():
    return { "message": "Hello from Automaton backend" }
```

Your frontend can call it with a relative path:

```js
const response = await fetch('/api/hello')
const data = await response.json()
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

## Deploy

```bash
automaton deploy
```

Deploy uploads the frontend and backend runtime.

If a Runner is already connected, backend APIs can become active during deploy.

If no Runner is connected yet, deploy can still upload the backend, but the backend will not run until you connect a Runner.

## Next step

Install a Runner before publishing backend apps you want people to use.
