# 05 Self-maintenance

Updated: 31 October 2026. Status: in progress (step 1 done for Actions; next: step 2, app permissions). Local nightly backups are shipped (server 0.1.19).

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
2. **App permissions.** An app declares requested permissions in `automaton.json` (for example `"permissions": ["backups:read"]`). Installing or changing them requires administrator approval in Studio; the platform then issues a scoped, revocable key and stores it as an app secret. Unapproved or revoked permissions are rejected by the API. *Acceptance:* a scoped key can call only its approved endpoints; revoking the approval stops the next call.
3. **Encrypted backup download (`backups:read`).** `backup.sh` additionally writes an encrypted archive of each nightly backup with a per-installation backup key; the API serves it read-only to `backups:read` keys. The backup key is shown once in **Admin → Installation → Backups** for the administrator to keep in a password manager; restoring off-site copies on a new server needs it. *Acceptance:* the archive decrypts only with the backup key and restores on another server.
4. **Template install and Admin → Maintenance.** The catalog, install, settings, permission approval, default schedules and update behavior described above.
5. **Off-site backup** (first maintenance app). Scheduled after the nightly backup: download the newest encrypted backup and upload it to S3-compatible storage (Hetzner Object Storage, Backblaze B2, AWS S3, MinIO) with retention. Studio shows its runs and logs. *Acceptance:* a server lost entirely is restored on a new server from the off-site copy plus the backup key.
6. **App data backup** (Action template for any app). Back up an app's own data directory (uploads, SQLite and similar files, which the platform database does not contain) on a schedule, to the same destinations, with a matching restore Action. *Acceptance:* deleted app data is restored for one app without affecting others.
7. **Further maintenance apps**, each only after its platform capability exists: disk-space and backup-age alerts by email, cleanup of unused Docker images and build cache within retention rules, certificate expiry checks.

## Other open work

Found during the clean-server installation review on 31 October 2026 (PRs #463–#468) or carried over from the former main roadmap. Kept here so it is not lost; most items are independent of the plan above.

- **Release artifacts and resource guidance.** Measure server-update downtime and publish CPU, memory and disk guidance from observed workloads (the updater replaces application containers in place; no zero-downtime updates). Publish portable CLI downloads as release assets if NuGet-only installation proves limiting.

- **Linux test job in CI.** CI builds images and runs CodeQL but no C# test suite. Add a Linux job for the API, CLI and Runner tests. Prerequisite: [repair tests that fail on main](03-enhancements.md#repair-tests-that-fail-on-main). On Windows, a filtered API run also fails dozens of pre-existing tests that may pass on Linux; establish the Linux baseline first.
- **Remove the 0.1.18-and-earlier update note** from [the install guide](../prod/install-automaton.md#update-an-installed-server) ("run update.sh a second time"). No external installation predates 0.1.19; both known servers are updated.
- **Studio Upgrade runner dialog.** The generated command is verified on two servers; the Studio dialog itself has not been opened by an administrator yet.
- **Docker Hub Runner repository** `rzvdaniel/automaton-runner`: no code, CI or running container uses it. Mark it deprecated or delete it (owner action on Docker Hub).
- **Production host housekeeping.** The operator home directory holds rollout leftovers from September–October (checkout/env backups, `rollout-backups`, `runner-image-rollback-*`, `stage4a-config-*`, logs); some may contain old secrets. Production also keeps `automaton-retained-*` images from its adoption and the unused `0f69…` Runner image. Review and remove when no longer needed.
- **"Hola Automaton"** on production has no Runner assignment since its retired Runners were removed. Redeploy it (`automaton deploy`) or delete it.
- **Off-host copy of `/etc/automaton`.** Restoring on a new server also needs the server secrets in `/etc/automaton`. Decide whether step 3's encrypted archive includes them (preferred) or stays database-and-keys only.
