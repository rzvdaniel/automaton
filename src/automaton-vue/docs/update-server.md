---
title: Update the server
description: Update an installed Automaton server to the latest release from the CLI or Studio, with SSH as the fallback.
---

# Update the server

This page updates the API, Worker, Studio and app store of your server to a newer release. Services can be briefly unavailable. Apps keep running on their Runners.

## Prerequisites

- You are a system administrator, and the CLI is signed in to the server: `automaton login --server https://api.example.com`.
- A server installed with the current installer. An older one needs the SSH fallback below once.
- Servers still on MySQL cannot take this update; move them first with the [migration guide](https://github.com/rzvdaniel/automaton/blob/main/docs/prod/migrate-to-postgresql.md).

## Steps

1. Update and wait.

   ```bash
   automaton server update --wait
   ```

   Pin a release with `--version server-vX.Y.Z`. The API restarts during the update; `--wait` follows it, then checks `/health` and the installed version, and exits nonzero on failure, printing the cause (the last line of `update.log`), the log tail and the recovery backup. The full log is on the server at `/var/lib/automaton/update/status/update.log`. `--timeout SECONDS` defaults to 1800.

2. In Studio instead: **Admin → Installation → Update** (the **Update to the latest release** button).

3. Upgrade the Runners. A server update does not change installed Runners; each keeps its old image until upgraded. From the CLI, `automaton server update --runners` waits for the update, names every Runner with its state and upgrades the ones on an old image (with only `--wait`, it lists them with the command to upgrade each; `automaton runners upgrade RUNNER --wait` needs a Developer API key).

   In Studio, open **Runners**: a Runner on an old image shows **Update available**; select **Upgrade → Upgrade now**, and Studio says whether it reconnected or rolled back. An offline Runner, or one busy with a deploy or an Action, cannot upgrade; retry when idle, or use **Run it manually** (shown there) for a Runner too old to upgrade itself.

4. Check the last run any time.

   ```bash
   automaton server update-status
   ```

A release older than the installed one is refused unless you pass `--allow-downgrade` (Studio does not offer it).

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

Expected: `ok`, and `Version:` shows the new release.

## Common errors

- **Not picked up within two minutes.** On the server run `systemctl status automaton-update.path`.
- **HTTP 409.** Another update is running; wait for it.
- **Update failed.** Read `/var/lib/automaton/update/status/update.log` (also shown by `automaton server update-status`). The previous release and a recovery backup are kept; the log names the backup. Do not rerun blindly. Restore with [Back up and restore](./backup-restore) if a migration already ran.

How the trigger works (the API never runs as root), adopted installs and recovery: [operator reference](https://github.com/rzvdaniel/automaton/blob/main/docs/prod/install-automaton.md#update-an-installed-server).
