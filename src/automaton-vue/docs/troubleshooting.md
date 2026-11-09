---
title: Troubleshooting
description: Fixes for apps that do not start after deploy, failing requests, login problems and blank pages.
---

# Troubleshooting

Common problems and how to fix them. For install problems see [Install with your AI agent](./install-with-ai) and [Install the server](./install-server#common-errors).

---

## App deployed but not running

**Symptom:** `automaton deploy` printed `Deploy complete`, but the app does not answer or `automaton apps` does not say `Running`.

- **Not finished yet** - `Deploy complete` only means the package was applied. Wait for `Running` in `automaton apps`.
- **No Runner online** - Check `automaton runners` (or **Runners** in Studio). Add or start a Runner, then deploy again.
- **Build or startup error** - Read `automaton logs` for the failing command and file or line.
- **Health check failure** - The app must listen on `AUTOMATON_PORT` and answer the health path in `automaton.json`.
- **Request to server code fails** - Deploy the changed code first; then check the route, method, body and the response status in the logs.

---

## Runner shows as Offline or Unknown

**Symptom:** `automaton runners` or the Runners page shows a Runner as offline after it started.

- **Port 5671** - The Runner connects outbound to the `api` host on port 5671 (TLS). It must be reachable from the Runner machine; the `api` host must not be proxied.
- **Runner stopped** - Check `docker ps` on the Runner host and the container's logs for startup errors.
- **Enrollment expired** - Run `automaton runners install` again; each command is single use.

---

## AI chat does not respond (App Editor)

**Symptom:** Typing a prompt in the App Editor AI chat produces no response.

**Fix:** Go to **Settings → AI Keys** and confirm that at least one valid AI provider key is saved. The editor will not make any AI requests until a key is configured.

---

## `automaton deploy` fails with authentication error

**Symptom:** `automaton deploy` exits with a 401 or 403 error.

- Sign in again: `automaton login --server <api-url>` (on a server without a browser, `automaton login --device`, see [Log in without a browser](./automaton-cli#log-in-without-a-browser)).
- Check which server is active with `automaton profiles` and `automaton whoami`.
- A user without permission on the app gets 403; ask the app's owner.

---

## The app loads but shows blank content

**Symptom:** The app loads without an error but the page is empty.

**Causes and fixes:**

- **Entry point mismatch** — Confirm that the public target's configured output directory contains the entry file expected by the runtime.
- **Build step not run** — Run the configured build command before deploying, or put that command and its output directory in `automaton.json` so the CLI can do it automatically.
- **JavaScript error on load** — Open the browser console in the preview and look for errors.
