---
title: Update the server
description: Update an installed Automaton server to the latest release from the CLI or Studio, with SSH as the fallback.
order: 90
---

# Update the server

This page updates the API, Worker, Studio and app store of your server to a newer release. Services can be briefly unavailable. Apps keep running on their Runners.

## What to ask the user

Confirm the update (services are briefly unavailable) and, for the SSH fallback, the root SSH login. Never ask for passwords or tokens. Updates take their own recovery backup.

## Prerequisites

- You are a system administrator, and the CLI is signed in to the server: `automaton login --server https://api.example.com`.
- A server installed with the current installer. An older one needs the SSH fallback below once.
- Servers still on MySQL cannot take this update; move them first, see [Move a MySQL server to PostgreSQL](#move-a-mysql-server-to-postgresql).

## Steps

The `automaton` commands run from your own machine, signed in to the server; only the `.sh` scripts and `systemctl` run on the server host.

1. Update and wait.

   ```bash
   automaton server update --wait
   ```

   Pin a release with `--version server-vX.Y.Z`. The API restarts during the update; `--wait` follows it, then checks `/health` and the installed version, and exits nonzero on failure, printing the cause (the last line of `update.log`), the log tail and the recovery backup. The full log is on the server at `/var/lib/automaton/update/status/update.log`. `--timeout SECONDS` defaults to 1800.

2. In Studio instead: **Admin → Installation → Update** (the **Update to the latest release** button).

3. Upgrade the Runners (separate: the update is already complete without this step). A server update does not change installed Runners; each keeps its old image until upgraded. From the CLI, `automaton server update --runners` waits for the update, names every Runner with its state and upgrades the ones on an old image (with only `--wait`, it lists them with the command to upgrade each; `automaton runners upgrade RUNNER --wait` needs a Developer API key).

   In Studio, open **Runners**: a Runner on an old image shows **Update available**; select **Upgrade → Upgrade now**, and Studio says whether it reconnected or rolled back. An offline Runner, or one busy with a deploy or an Action, cannot upgrade; retry when idle, or use **Run it manually** (shown there) for a Runner too old to upgrade itself.

4. Check the last run any time.

   ```bash
   automaton server update-status
   ```

A release older than the installed one is refused unless you pass `--allow-downgrade` (Studio does not offer it).

## Restart the server

Updates that need a restart (a new kernel) can be applied without SSH. A system administrator, or an app that holds the administrator-approved `server:restart` permission, asks the API to restart the server now or at a time within the next 7 days (`POST /api/install/restart` with an optional `{"restartAtUtc": "..."}`). The API never reboots the host: it writes a request file that the root unit `automaton-restart.path` turns into `systemctl reboot`. The request is refused while a backup, an app deploy, a server update or a Runner upgrade is running, and the host checks for a backup or update again when a scheduled restart is due (a deploy or Runner upgrade is checked only when the restart is requested) (a restart cancelled that way shows as refused in `GET /api/install/restart`). Every request is recorded in the audit log with who asked and for when, and `GET /api/install/restart` returns `lastRestartedAtUtc`. Apps come back by themselves after boot. The installer and the updater install the unit; `sudo bash /opt/automaton-server/restart-from-request.sh --install-units` enables it by hand, and `uninstall-automaton.sh` removes it.

## Fallback over SSH

```bash
sudo bash /opt/automaton-server/update.sh            # latest
sudo bash /opt/automaton-server/update.sh --version server-vX.Y.Z
```

## Verify it worked

```bash
curl -fsS https://api.example.com/health
automaton installation-status
```

Expected: `{"status":"ok"}` (the anonymous `/health` does not show the version), and `Version:` shows the new release.

## Common errors

- **Not picked up within two minutes.** On the server run `systemctl status automaton-update.path`.
- **HTTP 409.** Another update is running; wait for it.
- **Update failed.** If the services or the public checks (`/health`, Studio) fail after the update, the updater rolls back by itself: it restarts the previous release's images and, when a migration ran, restores the database from the update's recovery backup. Read `/var/lib/automaton/update/status/update.log` (also shown by `automaton server update-status`); it says whether the rollback worked and names the backup. If the rollback did not restore a healthy server, do not rerun blindly: restore with [Back up and restore](./backup-restore).

How the trigger works: the API never runs as root. It writes a request into `/var/lib/automaton/update/requests/`; the systemd path unit `automaton-update.path` starts `automaton-update.service`, which runs the installed `update.sh` as root (checksum check, recovery backup and health checks apply unchanged). One update runs at a time and every request is audited. Servers installed before this feature get the units the first time `update.sh` runs over SSH.

## What the updater does

The updater also refreshes the fail2ban jails (`sshd`, `automaton-scanner`, `automaton-signin`; your `ignoreip` is kept and bans block Docker's ports through `DOCKER-USER`). If an address is banned, run `sudo fail2ban-client unban <address>`.

It selects the newest stable release (public GHCR, no GitHub account), verifies the bundle checksum and version, and refuses a release that removes a service or changes PostgreSQL, RabbitMQ, Traefik, volumes, secrets or networks. With less than 10 GiB free it first removes unused Docker images and build cache (never volumes, images of existing containers or backups). It writes a root-only recovery directory under `/opt/automaton-server/backups` (PostgreSQL dump, configuration, API data, Data Protection keys, previous manifest), applies the new images, checks the public Studio/API routes, keeps the newest three update backups (`--keep-backups N`) and removes the previous release's images. For an offline transfer use `--bundle PATH --checksum PATH --version server-vX.Y.Z`. Copy recovery directories to encrypted off-host storage.

If an update fails after containers began changing, the updater goes back to the previous release and restores the database from its recovery backup when a migration ran (anything written during the failed update is lost). If that rollback itself fails, do not rerun blindly: restore the database, configuration and keys together ([Back up and restore](./backup-restore)). Servers on 0.1.18 or earlier run their previous updater for the first update: run `update.sh` a second time to enable nightly backups.

Servers older than the app store refuse that release from their old updater: fetch the current one once (`curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/update-automaton.sh -o update-automaton.sh`, read it, `sudo bash update-automaton.sh`) and add the `store.example.com` DNS record first so its certificate is issued right away.

## Move a MySQL server to PostgreSQL

Servers installed before PostgreSQL (`release.env` has a `MYSQL_IMAGE` line) keep their data in MySQL until `migrate-to-postgresql.sh` copies and verifies every table and switches the installation over. The MySQL volume is never modified, so you can go back. A standard installation made by `install.sh` is covered; an adopted installation needs a rehearsal first and, when its override contains the MySQL service, a separate `--postgres-override FILE` for the PostgreSQL stack.

Before you start: 10 GiB free (installation, Docker data directory and `--backup-dir`), internet access, a running server, an administrator account in `AUTOMATON_CHECK_EMAIL` / `AUTOMATON_CHECK_PASSWORD` (the script compares what the API lists before and after; optional `AUTOMATON_CHECK_USER_EMAIL` / `_PASSWORD` for a regular user), a provider snapshot, `tmux` or `screen`, and a quiet moment: API and Worker are down while the data is copied (about a minute for a small server). Apps keep running on their Runners. Rehearse on a restored backup on a scratch machine first (`--rehearsal`, never on the real server).

```bash
curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/docker/install/migrate-to-postgresql.sh -o migrate-to-postgresql.sh
less migrate-to-postgresql.sh
sudo bash migrate-to-postgresql.sh --version server-vX.Y.Z                         # report only
sudo -E bash migrate-to-postgresql.sh --version server-vX.Y.Z --run --confirm your-domain.example
```

The report lists tables to copy and work in flight (queued jobs, running Actions and Runner operations); wait for it or accept losing it with `--discard-in-flight`. The run takes a verified recovery backup (`backups/mysql-to-postgresql-<time>`), copies all tables in one transaction and verifies row counts, content hashes, foreign keys and sequences, starts the new API and Worker, checks the API snapshot and public routes, and then replaces the MySQL release files. Afterwards sign in, open an app and run an Action that reads a secret, and check schedules.

If it stops before the switch, it returns to MySQL by itself (if that fails, fix the reported problem and run `--rollback`, which is safe to repeat). After the switch, fix what it reports and run `--finish` with the same release and `AUTOMATON_CHECK_*` set; `--run` is refused so old MySQL data cannot overwrite PostgreSQL data. If your connection drops, run `--rollback` (or `--finish` if the recovery directory's `STATE` file says `switched`).

Go back with `sudo bash /opt/automaton-server/migrate-to-postgresql.sh --rollback --confirm your-domain.example`: it saves the PostgreSQL data to a dump, restores configuration and release files and restarts MySQL; anything written to PostgreSQL after the switch is lost. After a few days, remove the old database and recovery data; the script prints the exact names.
