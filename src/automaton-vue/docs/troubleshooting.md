# Troubleshooting

Common problems and how to fix them.

---

## Server-side execution is not active after deploy

**Symptom:** You deployed an app with server-side code, but its execution target is not running.

**Causes and fixes:**

- **No execution target available yet** — Deploy can still upload the server runtime successfully even when no execution target is online. In that case, bring one online and rerun deploy.
- **No runner connected in Studio** — Go to **Runners** and check that at least one runner shows as Online. If all runners are Offline or Unknown, server routes cannot become active yet.
- **Build or startup error** — Check the deploy log for the failing command and file or line number.
- **Health check failure** — Confirm that the runtime listens on `AUTOMATON_PORT` and serves a successful response at the configured health path.
- **No runner available** — Runtime files can upload successfully while execution waits for a runner. Assign or select a runner and deploy again.

---

## Requests to server-side code fail

**Symptom:** A client request to a same-origin server route fails.

**Causes and fixes:**

- **Runtime not deployed** — Deploy the changed runtime before reloading the preview.
- **Runtime uploaded but not running** — Rerun `automaton deploy --runner <runner-id-or-name>` after a runner is online.
- **Route or input mismatch** — Check the route, method, request body, and response status.
- **Runtime returned an error** — Inspect the response and runner logs for the failing command.

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

## The app loads but shows blank content

**Symptom:** The app loads without an error but the page is empty.

**Causes and fixes:**

- **Entry point mismatch** — Confirm that the public target's configured output directory contains the entry file expected by the runtime.
- **Build step not run** — Run the configured build command before deploying, or put that command and its output directory in `automaton.json` so the CLI can do it automatically.
- **JavaScript error on load** — Open the browser console in the preview and look for errors.
