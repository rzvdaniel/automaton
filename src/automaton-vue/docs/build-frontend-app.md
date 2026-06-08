---
title: Build a Frontend App
description: Build and host a static frontend app with AI using Automaton. Supports Vite, React, Vue, and vanilla JavaScript.
---

# Build a Frontend App

Frontend apps are hosted by Automaton.

Use this page when your app is static HTML/CSS/JavaScript, Vue, React, or another frontend framework.

## Create or link an app

```bash
automaton login
automaton new "My Frontend App"
```

This creates the Automaton app and writes the local `automaton.json` file.

If remote app creation fails, keep the generated local scaffold and continue building in the same folder. Link it later with `automaton create` or `automaton link <app-id>`.

## Frontend folder

For AI-generated apps and framework apps, keep frontend code in:

```txt
frontend/
```

Typical structure:

```txt
my-app/
  automaton.json
  .automatonignore
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

If you only have a local scaffold and no remote link yet, the same structure still applies. Add the `appId` later after `automaton create` or `automaton link`.

For Vite apps, set `base: './'` and keep app-local asset URLs relative so the published app works correctly under its slug path.

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

For a fuller frontend contract and starter examples, see:

- [Automaton frontend structure](https://automaton.monster/llms/automaton-frontend-structure.md)
- [Automaton examples](https://automaton.monster/llms/automaton-examples.md)
