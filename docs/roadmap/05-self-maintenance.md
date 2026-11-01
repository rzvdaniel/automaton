# 05 Self-maintenance

Updated: 31 October 2026. Status: in progress (steps 1, 1a and 1b done; next: step 2 app permissions). Work and status: parent issue [#490](https://github.com/rzvdaniel/automaton/issues/490) and its sub-issues; this file holds the design. Local nightly backups are shipped (server 0.1.19).

Automaton should maintain its own server with its own building blocks: apps, Actions and schedules. Maintenance logic lives in ordinary, premade apps instead of growing platform code for each task. The platform adds only small, generic capabilities those apps need. This also demonstrates the product: an Automaton server managing itself the same way users manage their apps.

Listed in the [roadmap index](README.md).

## Design

### Three layers

| Layer | Owns | Kept |
| --- | --- | --- |
| **Host basics** | The few operations that need root on the server, as fixed scripts or timers | As small as possible. Today: `backup.sh` (local nightly backup), `restore.sh`. |
| **Platform capabilities** | Narrow API endpoints that expose a host basic safely, each behind a named permission | Generic: no task-specific policy. |
| **Maintenance apps** | Policy: when to run, where to send, how many to keep, whom to notify | Ordinary apps with Actions and schedules, readable and replaceable. |

Maintenance apps get **only the permissions an administrator approves**: no hidden platform powers, no host or Docker access, no special execution path. A faulty or compromised maintenance app can do no more than its approved job. Tasks that truly need root stay in the host layer and are exposed to apps through a platform capability.

### What a maintenance app is

A source folder in this repository (for example `maintenance-apps/offsite-backup/`) with `automaton.json` and one or more Actions, written in Python, with unit tests that run in CI. It is premade, reviewed code: AI may help write it during development, but installation never generates code. Each server release builds its templates into the API image, so a server only installs templates matching its version and never downloads app code at runtime.

### Delivery

- **Admin → Maintenance** in Studio lists the templates of the server's release: purpose, requested permissions, installed version and settings.
- **Install** (system administrators): the API creates an ordinary app owned by the administrator, deploys the template package through the existing durable deployment pipeline (as `automaton deploy` does), assigns the Runner by the existing rule (the only online Machine Management Runner, or the administrator's choice), asks for the template's settings and secrets, shows the requested permissions for approval, and creates the template's default schedules with the existing Action schedules.
- **Not installed by the server installer**: a fresh server has no Runner yet and no destination credentials. Studio suggests the relevant maintenance apps once a Runner is online, for example "Set up off-site backups".
- **Updates**: a server update brings newer templates. Proposed default: unmodified maintenance apps update automatically with the server; once an administrator edits an app's source, Studio shows "Update available" and the administrator decides. Settings, secrets, approvals and schedules are kept across updates.

## Plan

Each step is generic platform work or one maintenance app, delivered and verified on its own. Steps 1–3 are prerequisites for step 5.

1. **App secrets** — *done for Actions* (this PR): **App → Settings → Secrets** and `api/apps/{id}/secrets` (write-only values, owner/updater permission), stored with the existing encrypted secret store, delivered to every Action run and redacted from logs and results; deleted with the app. Runtime delivery is deferred: the Runner restarts runtimes from local metadata without the API, so runtime secrets need a Runner-side design. Original scope: encrypted per-app secrets (Data Protection key ring, like Studio-managed SMTP), set in Studio and by API, never returned after saving, passed as environment variables to the app's Actions and runtime. Generic: every app can use them. *Acceptance:* a secret set in Studio reaches an Action run, is absent from logs, API responses and source, and survives backup/restore.
1a. **Secrets for running apps** — *done* (this PR): the Runner loads the current secrets from `api/runner-runtime/apps/{app}/targets/{target}/secrets` (Runner credential; only targets assigned to that Runner) before creating each runtime container, on deploy and on every restart; a failed load stops that start and the existing reconcile retry tries again. Values are environment variables. Not yet: a Studio restart action (changes apply on the next start; redeploy), Compose apps, file delivery. Original scope: Deliver app secrets to HTTP runtimes (APIs, server-rendered sites), not only Actions. The Runner restarts runtimes on its own, so it fetches the app's current secrets from the API with its Runner credential each time it starts a runtime (only for apps assigned to it) and retries instead of starting without them. Values are passed as environment variables, as for Actions; file delivery under `/run/secrets` can follow. Saving a secret offers to restart the running app. Static sites cannot use secrets (everything ships to the browser): Studio and the CLI say so. Build-time secrets are separate and later. *Acceptance:* a runtime reads a secret after deploy, after a Runner restart and after a host reboot; a changed secret applies after the offered restart; values never appear in logs.
1b. **Shared vault** — *done* (this PR): declarations in `application.secrets` (`name`, `description`, `from`), resolution app → project vault (apps in exactly one project) → owner's vault (personal apps only, so project members cannot pull a person's own secrets into code they control), app-relative API `api/apps/{id}/secrets?scope=app|project|user` with `POST …/status`, and `automaton secrets status|list|set|delete` (values from standard input); `validate` and `deploy` warn about missing ones. Declarations live in `application` rather than at the top level so they are stored with every deploy. Not yet: Studio pages for the project and user vaults, audit records of changes and uses (no audit log exists yet). Original scope: Secrets owned by a project or a user and used by several apps, built on the same store (which already supports these owner types). Designed for AI agents rather than browsing: flat names per scope with an optional one-line description, no folders or collections.
    - Apps declare the secrets they need in `automaton.json`: `"secrets": [{ "name": "STRIPE_KEY", "description": "..." }]`.
    - Resolution by name: app secret, then project vault, then the user's vault; an optional `"from": "project:STRIPE_LIVE_KEY"` maps a different name.
    - CLI: `automaton secrets status` (declared secrets: present, missing, providing scope), `automaton secrets set NAME [--project|--user]` reading the value from standard input so it never lands in history or AI chat, `list`, `delete`. `validate` and `deploy` warn about missing declared secrets.
    - Rotation: a new version applies to every app using it at its next Action run or offered restart. Changes and uses are recorded in the audit log.
    - *Acceptance:* one project secret used by two apps; rotating it updates both; an agent can find and fill a missing declared secret with the CLI alone. *Passed* on the test server with 0.1.25.
    - Follow-ups: Studio vault pages (#478), audit log (#479), projects API for API keys (#480), older CLIs dropping fields (#481), stale secrets in stopped containers (#482).
2. **App permissions** (#483). An app declares requested permissions in `automaton.json` (for example `"permissions": ["backups:read"]`). Installing or changing them requires administrator approval in Studio; the platform then issues a scoped, revocable key and stores it as an app secret. Unapproved or revoked permissions are rejected by the API. *Acceptance:* a scoped key can call only its approved endpoints; revoking the approval stops the next call.
3. **Encrypted backup download (`backups:read`)** (#484). `backup.sh` additionally writes an encrypted archive of each nightly backup with a per-installation backup key; the API serves it read-only to `backups:read` keys. The backup key is shown once in **Admin → Installation → Backups** for the administrator to keep in a password manager; restoring off-site copies on a new server needs it. *Acceptance:* the archive decrypts only with the backup key and restores on another server.
4. **Template install and Admin → Maintenance** (#485). The catalog, install, settings, permission approval, default schedules and update behavior described above.
5. **Off-site backup** (first maintenance app, #486). Scheduled after the nightly backup: download the newest encrypted backup and upload it to S3-compatible storage (Hetzner Object Storage, Backblaze B2, AWS S3, MinIO) with retention. Studio shows its runs and logs. *Acceptance:* a server lost entirely is restored on a new server from the off-site copy plus the backup key.
6. **App data backup** (Action template for any app, #487). Back up an app's own data directory (uploads, SQLite and similar files, which the platform database does not contain) on a schedule, to the same destinations, with a matching restore Action. *Acceptance:* deleted app data is restored for one app without affecting others.
7. **Further maintenance apps** (#488), each only after its platform capability exists: disk-space and backup-age alerts by email, cleanup of unused Docker images and build cache within retention rules, certificate expiry checks.

## Other open work

Found during the clean-server installation review on 31 October 2026 (PRs #463–#468) or carried over from the former main roadmap. Kept here so it is not lost; most items are independent of the plan above.

- **Release artifacts and resource guidance.** Measure server-update downtime and publish CPU, memory and disk guidance from observed workloads (the updater replaces application containers in place; no zero-downtime updates). Publish portable CLI downloads as release assets if NuGet-only installation proves limiting.

- **Linux test job in CI** (#489). CI builds images and runs CodeQL but no C# test suite. Add a Linux job for the API, CLI and Runner tests. Prerequisite: [repair tests that fail on main](03-enhancements.md#repair-tests-that-fail-on-main). On Windows, a filtered API run also fails dozens of pre-existing tests that may pass on Linux; establish the Linux baseline first.
- **Remove the 0.1.18-and-earlier update note** from [the install guide](../prod/install-automaton.md#update-an-installed-server) ("run update.sh a second time"). No external installation predates 0.1.19; both known servers are updated.
- **Studio Upgrade runner dialog.** The generated command is verified on two servers; the Studio dialog itself has not been opened by an administrator yet.
- **Docker Hub Runner repository** `rzvdaniel/automaton-runner`: no code, CI or running container uses it. Mark it deprecated or delete it (owner action on Docker Hub).
- **Production host housekeeping.** The operator home directory holds rollout leftovers from September–October (checkout/env backups, `rollout-backups`, `runner-image-rollback-*`, `stage4a-config-*`, logs); some may contain old secrets. Production also keeps `automaton-retained-*` images from its adoption and the unused `0f69…` Runner image. Review and remove when no longer needed.
- **"Hola Automaton"** on production has no Runner assignment since its retired Runners were removed. Redeploy it (`automaton deploy`) or delete it.
- **Off-host copy of `/etc/automaton`.** Restoring on a new server also needs the server secrets in `/etc/automaton`. Decide whether step 3's encrypted archive includes them (preferred) or stays database-and-keys only.
