# Troubleshooting

Common problems and how to fix them.

---

## Backend APIs are not active after deploy

**Symptom:** You deployed an app with backend code, but the backend APIs are still not running.

**Causes and fixes:**

- **No execution target available yet** — Deploy can still upload backend runtime successfully even when no execution target is online. In that case, bring one online and rerun deploy.
- **No runner connected in Studio** — Go to **Runners** and check that at least one runner shows as Online. If all runners are Offline or Unknown, backend APIs cannot become active yet.
- **Python syntax error** — A syntax error in any `backend/*.py` file will stop the deploy. Check the deploy log for the file and line number.
- **Missing `handle()` export** — Every controller file must export a top-level `handle(action_name, inputs)` function. A file that is missing `handle` will cause the deploy to fail.
- **No runner available** — Backend runtime can be uploaded successfully but still not be running. If deploy reports that backend was uploaded but is not running, assign/select a runner and rerun deploy.

---

## `callController` throws in the frontend

**Symptom:** The frontend throws an error when calling `window.automatonApi.callController(...)`.

**Causes and fixes:**

- **Backend not deployed** — Changes to Python files need a Deploy Backend step before they take effect. Deploy, then reload the preview.
- **Backend uploaded but not running** — For CLI deploys, frontend and backend runtime may already be synced even when no runner is available. In that case rerun `automaton deploy --runner <runner-id-or-name>` after a runner is online.
- **Wrong controller or action name** — The controller name is the filename without `.py`. The action name is the method name on the controller class. Check spelling.
- **Action returned an error** — `callController` throws when the backend returns `{ success: false, ... }`. Add `try/catch` around every call and log `err.message` to see the backend error.
- **Local development without mocks** — When running locally (not in the Automaton preview), `callController` reads from `backend/mocks/{controller}.{action}.json`. If that file is missing, the call throws. Add a mock file.

---

## Runner shows as Offline or Unknown

**Symptom:** The Runners page shows a runner as Offline or Unknown even after it started.

**Causes and fixes:**

- **Server URL mismatch** — The runner must be configured with the same server URL that the Studio is using. Check `AUTOMATON_SERVER_URL` in the runner config.
- **API key not valid** — Confirm the runner API key is active (it should have runner scope, not execute scope). Rotate the key in API Keys settings if needed.
- **Firewall / network issue** — The runner connects outbound over HTTPS to the Studio server. Make sure port 443 (or the port your server uses) is not blocked.
- **Runner crashed on startup** — Run the runner manually (`automaton-runner` or `docker compose logs runner`) and look for startup errors.

---

## AI chat does not respond (App Editor)

**Symptom:** Typing a prompt in the App Editor AI chat produces no response.

**Fix:** Go to **Settings → AI Keys** and confirm that at least one valid AI provider key is saved. The editor will not make any AI requests until a key is configured.

---

## `automaton deploy` fails with authentication error

**Symptom:** `automaton deploy` exits with a 401 or 403 error.

**Fixes:**

- Run `automaton login --server <url> --key <your-key>` again to refresh the stored credentials, or use `automaton config --server <url> --key <your-key>` if you want explicit manual configuration.
- Confirm the API key has `execute` scope (not read-only).
- Confirm the server URL in `automaton.json` matches the server you logged into.

---

## Frontend loads but shows blank content

**Symptom:** The app loads without an error but the page is empty.

**Causes and fixes:**

- **Entry point mismatch** — For explicit no-build static apps, `index.html` can live at the project root with `"frontend": { "deployDir": "." }`. For build-based apps, the deployable entry point should be `frontend/dist/index.html`. Confirm that `frontend.deployDir` matches the folder you intend to upload.
- **Framework build not run** — For default Vite apps, run `cd frontend && npm run build` before deploying, or set `"frontend": { "build": "cd frontend && npm install && npm run build", "deployDir": "frontend/dist" }` in `automaton.json` so the CLI does it automatically.
- **JavaScript error on load** — Open the browser console in the preview and look for errors.
