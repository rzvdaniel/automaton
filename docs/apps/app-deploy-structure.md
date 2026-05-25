# Automaton App Deploy Structure

This document explains the current deploy contract. It replaces the old "one folder is both source and live output" model.

## Core model

Automaton separates editable source from deployed live output, and each side has frontend/backend surfaces:

```text
AppData/apps/{appId}/
├─ source/
│  ├─ frontend/
│  └─ backend/
└─ live/
   ├─ frontend/
   └─ backend/
```

Rules:
- `source/frontend/` is editable frontend project source.
- `source/backend/` is editable backend Python source.
- `live/frontend/` is the only public static output.
- `live/backend/` is runner-only backend output and is never publicly served.
- Fork copies `source/` only, never `live/`.

## CLI deploy contract

For local CLI apps, `automaton.json` now uses explicit nested config:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
```

Deploy behavior:
1. Run `frontend.build` when configured.
2. Upload `frontend.deployDir` into `live/frontend/`.
3. Upload `backend.deployDir` into `live/backend/` when configured.
4. Trigger backend runner deployment from `live/backend/`.

There is no automatic fallback to `dist/`, `frontend/`, or project root. `frontend.deployDir` must be explicit unless the caller intentionally overrides it with `automaton deploy --dir ...`.

## Recommended local project layout

Default AI-generated local app:

```text
my-app/
├─ automaton.json
├─ frontend/
│  ├─ src/
│  ├─ public/
│  ├─ dist/
│  ├─ package.json
│  └─ vite.config.ts
└─ backend/
   ├─ *.py
   ├─ requirements.txt
   ├─ shared/
   └─ mocks/
```

Why this is the default:
- Vite gives hashed asset filenames, which reduces stale CSS/JS behind Cloudflare.
- Vite can emit relative app-local URLs with `base: './'`, which is required for slug-hosted published apps.
- Public runtime is a clean build artifact from `frontend/dist/`.
- Backend runtime is packaged separately, so frontend output does not need to contain backend files.

## Asset versioning rule

For build-based apps:
- `index.html` may keep a stable filename.
- CSS, JS, images, fonts, manifests, and worker files should use hashed filenames when the build system supports it.
- Do not hardcode unversioned production asset paths like `/assets/index.css` or `/assets/app.js`.
- Do not emit root-relative app-local URLs such as `/assets/...`, `/manifest.webmanifest`, `/favicon.ico`, or `/`.
- Published app validation rejects root-relative app-local URLs. For Vite, set `base: './'` in `frontend/vite.config.ts`.
- Do not rely on Automaton Studio mutating app HTML to add `?v=` cache-busting.
- Automaton is responsible for cache headers, CORS headers, and runtime asset serving.

## Explicit no-build static exception

Plain root HTML/CSS/JS remains supported only as an explicit no-build option:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "frontend": {
    "deployDir": "."
  }
}
```

Use this only for very small static demos. It is not the default for AI-generated apps.

## What to avoid

Avoid these assumptions:
- top-level `build` and `deployDir` still exist in `automaton.json`
- frontend and backend should be merged into one runtime folder
- raw frontend source files should appear in public runtime
- `frontend/src/`, `package.json`, or `vite.config.*` should be publicly visible after deploy

## Summary

- Public runtime comes only from `frontend.deployDir`.
- Runner backend runtime comes only from `backend.deployDir`.
- Default AI-generated local apps should use `frontend/` + Vite and deploy only `frontend/dist/`.
- Root static apps remain supported, but only as an explicit no-build exception.

