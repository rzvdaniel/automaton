# Frontend And Docs Local Workflow

This is the current local development command model for the website, Studio, and documentation.

## Default app startup

The shared local startup script does not start any documentation server by default:

```powershell
.\scripts\dev-start.ps1
```

That script starts:

- Automaton API
- Automaton Worker
- Runner FastAPI backend
- Automaton Runner
- Studio app
- Website app

It does not start the public docs VitePress server.

## Studio commands

From [src/automaton-studio-vue](/C:/Work/automaton/src/automaton-studio-vue):

```powershell
npm run dev
```

Starts the Studio app only.

Admin help content is loaded inside Studio through the authenticated documentation drawer.

Source markdown lives in [src/Automaton.Api/Documentation/Admin](/C:/Work/automaton/src/Automaton.Api/Documentation/Admin).

## Website commands

From [src/automaton-vue](/C:/Work/automaton/src/automaton-vue):

```powershell
npm run dev
```

Starts the website app only.

```powershell
npm run docs:dev
```

Starts the public docs VitePress server only.

```powershell
npm run docs:build
```

Builds the public docs site.

```powershell
npm run dev:full
```

Starts both the website app and the public docs server when you explicitly want both.

## Naming convention

The website and Studio now follow these rules:

- `npm run dev` = app only
- website `npm run docs:dev` = public docs only
- website `npm run docs:build` = public docs build

Only the website keeps these extra docs commands:

- `npm run docs:dev` = docs only
- `npm run docs:build` = docs build
- `npm run dev:full` = app + docs

## When to use docs servers

Start the public docs VitePress server only when you are actively editing website documentation and want live preview.

For admin help content, edit the markdown under [src/Automaton.Api/Documentation/Admin](/C:/Work/automaton/src/Automaton.Api/Documentation/Admin) and verify it through the Studio Help drawer.
