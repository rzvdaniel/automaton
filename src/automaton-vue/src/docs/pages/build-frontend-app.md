# Build a Frontend App

Frontend apps are hosted by Automaton.

Use this page when your app is static HTML/CSS/JavaScript, Vue, React, or another frontend framework.

## Create or link an app

```bash
automaton login
automaton new "My Frontend App"
```

This creates the Automaton app and writes the local `automaton.json` file.

## Frontend folder

For AI-generated apps and framework apps, keep frontend code in:

```txt
frontend/
```

Typical structure:

```txt
my-app/
  automaton.json
  frontend/
    package.json
    src/
```

## Configure the frontend build

For a normal Vite app, `automaton.json` should include:

```json
{
  "appId": "your-app-id",
  "name": "My Frontend App",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  }
}
```

`frontend.build` runs before upload.

`frontend.deployDir` is the folder Automaton serves publicly after deploy.

## Deploy

```bash
automaton deploy
```

## Publish when ready

```bash
automaton publish
```

## Frontend-only apps do not need a Runner

A Runner is only needed when your app has backend code.
