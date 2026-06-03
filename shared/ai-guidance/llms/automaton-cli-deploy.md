# Automaton CLI Deploy Workflow

Use this when the user is building an Automaton app locally in Cursor, Copilot, Claude Code, or another IDE and wants to deploy it with the Automaton CLI.

## Goal

Keep the project local, commit it to Git, and deploy the app to Automaton with the CLI.

For now, treat Automaton Studio as the official preview and debugging environment after each deploy.

## Prerequisites

1. The Automaton CLI is installed.
2. The current machine or workspace has already been prepared with `automaton setup`.
3. If install or first-time setup is still needed, read `automaton-cli-install.md` first.

3. The AI should create `automaton.json` in the project root automatically when it follows the local generated guidance after setup, or `llms.txt` as the fallback source of truth.

When the user asks to use the Automaton CLI, the AI should prefer `automaton new "<app-name>"` for a new app or `automaton link <app-id>` for an existing one, and should not stop before `automaton.json` exists in the project root, unless it clearly reports why that creation/link step failed.

If Automaton MCP tools are already available in the AI environment, they may be used as an optional inspection layer for existing apps. See `automaton-mcp.md`. Do not block the normal CLI workflow on MCP setup.

The CLI also creates `.automatonignore` in the project root when the project is initialized or linked, if the file does not already exist. It starts with the built-in deploy exclusions like `.automaton/`, `.git/`, `node_modules/`, `bin/`, `obj/`, `automaton.json`, and `.automatonignore` itself.

Automaton separates source and runtime, with frontend/backend surfaces. The local project is source. `automaton deploy` uploads `frontend.deployDir` into `runtime/frontend/` and `backend.deployDir` into `runtime/backend/` when present. Public app serving reads only `runtime/frontend/`. Studio source editing, GitHub sync, version history, archive import/export, and fork use source.

`automaton setup`, `automaton new`, and `automaton link` also sync canonical local AI guidance under `.automaton/agents/`, including references under `.automaton/agents/references/`, and write a small root `AGENTS.md` adapter. If those generated files are missing or stale, run:

```bash
automaton sync-ai
```

Optional tool adapters can be generated later without changing the default setup flow:

```bash
automaton sync-ai --adapter claude
automaton sync-ai --adapter copilot
automaton sync-ai --adapter all
```

4. If the app already exists in Automaton Studio, link the local project to it:

```bash
automaton link <app-id>
```

If the app is being created from scratch locally, skip this step. `link` is only for attaching the local folder to an already-existing app.

## Autonomous CLI-first flow

Real happy path:

1. install the CLI once
2. run `automaton setup`
3. let the AI handle most of the remaining local work
4. run `automaton new "My App"` or `automaton link <app-id>`
5. run `automaton deploy`
6. if `backend/*.py` exists, let deploy auto-select or prompt for a connected runner
7. preview and debug in Automaton Studio

Use this canonical order:

```bash
automaton setup
automaton new "My App"
automaton deploy
# ask user before publish
automaton publish --yes
automaton status
```

For non-interactive AI, CI, and script workflows, do not rely on deploy prompting for publish later. Use explicit intent:

```bash
automaton deploy --non-interactive
automaton deploy --publish --yes --non-interactive
automaton publish --yes --non-interactive
```

The CLI should also auto-switch to non-interactive behavior when `CI=true`, `AUTOMATON_CI=true`, or stdio is redirected. In that mode it should avoid prompts, selection UIs, progress redraws, and rich tables when plain append-only logs are safer.

Interpretation rules:

1. Run `automaton setup` first so the AI can authenticate and sync local guidance in one step.
2. After `automaton setup`, the AI should be able to handle most of the remaining workflow: create or link the app, scaffold files, keep `automaton.json` correct, and deploy.
3. If testing requires a clean auth state or a different account, use `automaton logout`, `automaton logout --global`, or `automaton logout --all` before re-running `automaton setup`.
4. Use `automaton new` for new apps; reserve `automaton link` for existing remote apps.
5. Run `automaton sync-ai` only when the generated local guidance files need a manual refresh.
6. Run `automaton deploy`; it performs deploy preflight and handles runner selection only when backend controllers require it.
7. If backend controllers exist and no connected runner is available, ask for confirmation and prefer `automaton runner install` to create a local Docker runner. Tell the user to use Automaton Studio runner setup only when the CLI path is unavailable or they explicitly prefer it.
8. After `automaton deploy`, open the app in Automaton Studio, use Preview there, and inspect logs there before considering publish.
9. Ask the user before public publish, then use `automaton publish --yes` only after approval.
10. For installable PWAs, validate install behavior from the published `apps.automaton.monster/{slug}/` URL, not from the Studio preview URL.

## Current test and debug loop

Until a dedicated Automaton local runtime exists, use this workflow:

1. Build the app locally in your IDE.
2. Start the local frontend dev server.
3. If the app needs backend-like data, use a local mock backend or mock data.
4. Test locally first with your browser or an AI browser tool.
5. Fix local UI/runtime issues before deploying.
6. Run `automaton deploy`.
7. Open the app in Automaton Studio.
8. Preview the deployed app.
9. Inspect frontend/backend logs in Studio.
10. Fix locally and redeploy if needed.
11. Publish only after the Studio preview looks correct.

Lifecycle: local mock/test -> deploy -> Studio integration test -> publish.

Local testing catches frontend and UX issues quickly. Studio preview catches deployed integration issues, including Automaton backend routing, runner behavior, and runtime logs.

## AI browser testing

For Codex, run `/browser` once per session if needed.

Before initialization, browser targets may be empty:

```ts
await agent.browsers.list()
// []
```

After initialization, the in-app browser target should appear, usually as `iab`.

Use it to test the local app on:

```text
http://127.0.0.1:<port>
```

The AI can inspect DOM state, console errors, screenshots, and click/navigation flows before deployment.

Do not claim local browser testing was completed unless the app was actually opened in a browser automation target or a normal browser and visible state, console errors, and at least the main screen were inspected.

## Local backend mocking

If the frontend needs backend-like data before deployment, add a local mock backend or mock data mode.

Preferred options:

- MSW for Vite, React, and Vue browser mocks
- json-server for simple CRUD data
- Prism for OpenAPI-based mocks
- Mockoon CLI for richer mock API servers

Do not treat the mock server as the production backend.

Keep the real Automaton backend in `backend/` and implement it using Automaton Python controllers when deployed backend behavior is needed.

## What `automaton validate` is for

`automaton validate` is an optional diagnostic preflight. `automaton deploy` runs the deploy checks needed for the happy path.

Use it to catch obvious problems early, such as:

- missing or invalid `automaton.json`
- missing app linkage
- missing frontend entry files or invalid deploy configuration
- backend controllers that require a runner when none is assigned
- other structural issues that should fail before deploy starts

If `automaton validate` passes but `automaton deploy` fails during upload with transport-style errors such as closed stream, multipart/request stream, socket, timeout, or HTTP pipeline failures, do not assume the app structure is wrong. Treat that as a likely CLI or server upload-path problem first, unless the error message clearly points to an invalid deploy directory or archive shape.

## What the AI should do

- Build the app in the current local project folder.
- Keep `automaton.json` in the project root.
- Keep `.automatonignore` in the project root and add custom deploy excludes there when needed.
- Follow the local generated guidance pack after setup. Start with `.automaton/agents/` when present, then use `.automaton/agents/references/llms.txt` and the linked files as the detailed canonical source. Use the public `llms.txt` only as a fallback when local guidance is unavailable.
- Treat `automaton sync-ai` as the manual refresh path for the generated local guidance pack, not as a required onboarding step.
- For AI-generated local apps, always set `frontend.build` and `frontend.deployDir` in `automaton.json`.
- Default to frontend-only. Create `backend/` only when the user-requested core feature requires server-side execution, secrets, persistence, auth, scheduled work, or browser-unsafe access that cannot be removed without breaking the requested app.
- Do not create `backend/` only for convenience, caching, minor proxying, CORS or mixed-content workarounds for non-essential data, or optional enrichment.
- When backend controllers are required, implement Automaton Python controllers that export `handle(action_name, inputs)`. Do not create a standalone FastAPI, Flask, or other web server under `backend/`.
- If the app is build-based and also has Python backend controllers, keep backend source in `backend/`. Do not copy backend files into frontend `deployDir`; deploy uploads frontend runtime and backend runtime separately.
- Use Python backend controllers in `backend/`.
- If the app includes Python backend controllers, do not consider backend deploy complete until `automaton deploy` assigned or used a runner, or you have clearly reported why it could not be made.
- Do not consider local build, type-check, tests, or `python -m py_compile` to be completion. Before declaring the app done, run `automaton deploy`; if it fails, report the exact command, exact error, and the next step.
- Prefer `automaton status` after deploy before declaring success.
- After deploy, use Automaton Studio preview and logs as the current test/debug loop before proposing publish.
- Before deploy, test locally first with a browser or AI browser tool, and use local mocks when backend-like data is needed.
- Ask before public publish, then use `automaton publish --yes` after approval.
- Do not rely on deploy asking whether to publish next. Use explicit publish commands or flags.
- For AI and CI workflows, prefer `--non-interactive` or `--json` so output stays deterministic and append-only.
- If upload fails after validation already passed, avoid repeatedly restructuring a plain app unless there is clear evidence that `deployDir`, the archive shape, or duplicate deploy roots are the actual problem.

## Deploy commands

```bash
automaton deploy
automaton deploy --json
automaton deploy --clean
automaton deploy --skip-backend
```

For backend apps, let deploy choose or prompt for a connected runner:

```bash
automaton deploy
automaton deploy --runner <runner-id-or-name>
```

`automaton deploy` uploads the frontend archive to `runtime/frontend/`, uploads backend files to `runtime/backend/` when present, and then deploys backend runtime scripts using the same API key authentication path. If the app has no backend scripts, a successful deploy may end with `No backend scripts found`.

## Framework apps

For AI-generated local apps, default to Vite + vanilla TypeScript or JavaScript in `frontend/`. Public runtime must come from `frontend/dist/`. Vite hashed assets reduce stale CSS/JS caching behind Cloudflare; `index.html` should still revalidate on the server. Set `base: './'` in `frontend/vite.config.ts` so the published build uses relative asset URLs under `apps.automaton.monster/{slug}/`.

For React, Vue, Vite vanilla, or other build-based apps, keep the source in the repo and make the deploy target match the final runtime shape.

For build-based frontend apps, keep build-generated hashed asset filenames. Do not add cache-busting by rewriting deployed HTML in Studio or by manually appending random `?v=` query strings in app source unless there is no build system available. Do not emit root-relative app-local URLs such as `/assets/...`, `/manifest.webmanifest`, `/favicon.ico`, or `/`; publish validation rejects them because published apps live under a slug path.
Do not hardcode `automaton-config.js`, `automaton-api.js`, or other Automaton runtime script tags into app source. The platform injects those during publish.
Keep service worker files and registrations relative to the published slug scope; avoid root-relative worker URLs.

Canonical installable PWA pattern:
- `frontend/index.html` links `./manifest.webmanifest`
- `frontend/public/manifest.webmanifest` uses `id`, `start_url`, `scope`, and icon `src` values rooted at `./`
- `frontend/src/main.ts` listens for `beforeinstallprompt` and registers `./sw.js`
- `frontend/public/sw.js` exists when the app should be installable offline or install-friendly

Responsibility boundary:
- App build: hashed CSS, JS, image, font, manifest, and worker asset filenames when supported.
- App build: relative app-local URLs. For Vite, this means `base: './'`.
- Automaton platform: cache headers, CORS headers, and runtime asset serving.
- Automaton platform: publish-time injection of platform runtime scripts.

Do not move Vite output to root `dist/` unless the user explicitly requested a no-build static app or Automaton CLI output proves that `frontend/dist/` cannot work.

Frontend-only example `automaton.json`:

```json
{
    "appId": "your-app-id",
    "name": "My App",
    "server": "https://api.automaton.monster",
    "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  }
}
```

Frontend-plus-backend example `automaton.json`:

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

If `automaton.json` already exists, the AI should add missing `frontend.build` and `frontend.deployDir` fields for build-based local apps. Use root deploy only when the user explicitly asked for a no-build static app.

## Default instruction to the AI

```text
Build [describe the app] for Automaton Monster using the Automaton CLI. Start by running automaton setup, then follow the generated .automaton guidance in this folder. If local guidance is unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy and ask before publish. Only ask me to step in for authentication or publish approval.
```

When the app is deployed but not yet published, the AI should direct the user to preview it in Automaton Studio and inspect logs there before recommending publish.

If the user asks to switch accounts or reset stored auth during testing, use `automaton logout` before logging in again.
