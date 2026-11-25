---
title: When something goes wrong
description: Symptoms, causes and fixes for an app that is down, a Runner that is offline, a failed update, low disk, sign-in problems and a blank page, with short answers to common questions.
order: 140
---

# When something goes wrong

Find your symptom, check the likely cause, apply the fix. If your AI built the app, you can also tell it what you see in your own words: it reads the logs and fixes the code.

## An app does not answer (503 or timeout)

Open **Apps** in Studio and read the app's status, or run `automaton apps`.

| What you see | Cause | Fix |
| --- | --- | --- |
| Not `Running` yet after `Deploy complete` | That message only means the package was applied | Wait for `Running` |
| **Runner offline** | The app's Runner is not connected | See [A Runner is offline](#a-runner-is-offline) |
| **Failed** | Build or startup error | `automaton logs` shows the failing command, file or line |
| Runs, but requests fail | The app must listen on `AUTOMATON_PORT` and answer the health path in `automaton.json` | Fix the port or health path, deploy again |
| A server-code request fails after a change | The changed code is not deployed | Deploy, then check the route, method, body and the status in the logs |

Starting an Action or reading its logs returns **503** "It may be offline" when the Runner does not answer within 60 seconds.

## A Runner is offline

`automaton runners` or **Server → Runners** shows it as offline. Studio marks a Runner offline after about ten minutes without a heartbeat.

- **Port 5671.** The Runner connects outbound to the `api` host on port 5671 (TLS). The Runner machine must reach it, and the `api` DNS record must not be proxied.
- **Runner stopped.** On the Runner host run `docker ps` and read the container's logs for startup errors.
- **Enrollment expired.** Run `automaton runners install` again; each command is single use.
- **The machine is gone for good.** An administrator can delete the Runner and its apps, see [Update the server and Runners](./install#update-the-server-and-runners).

## An update failed

If the services or the public checks fail after an update, the updater goes back to the previous release by itself and, when a migration ran, restores the database from the update's recovery backup. Anything written during the failed update is lost.

1. Run `automaton server update-status`. It shows the last line of `update.log`, which says whether the rollback worked.
2. Check the server: `curl -fsS https://api.yourdomain.com/health` should print `{"status":"ok"}`.
3. If it is healthy, you are on the previous release; fix the cause the log names and run the update again.
4. If the rollback did not restore a healthy server, do not rerun blindly. Restore the database, configuration and keys together: [Back up and restore](./backup-restore).

More in [Update the server](./install#update-the-server-and-runners).

## The disk is nearly full

Studio shows memory and disk under **Server** (see [A tour of Studio](./studio-tour)); `df -h` on the server shows free space.

- **Unused Docker data.** A nightly cleanup removes unused Docker images and build cache older than 7 days and trims large journal files. It never touches volumes, images of existing containers or backups. See [Back up and restore](./backup-restore).
- **Find what is big.** `sudo du -xh --max-depth=1 /var/lib/docker | sort -h | tail` on the server.
- **Backups.** Nightly backups are kept for 7 days on the same disk. Copy them elsewhere if you need more.
- **Never** run `docker compose down -v` or `docker volume prune`: they delete data.

## I cannot sign in or deploy (401 or 403)

- Sign in again: `automaton login --server <api-url>` (no browser: `automaton login --device`).
- Check the active server with `automaton profiles` and `automaton whoami`.
- A user without permission on the app gets 403. Ask the app's owner.
- Nobody can sign up on their own. An administrator creates or invites each user.
- Too many failed sign-ins ban your address for an hour. See [A banned address](#a-banned-address).

## The app loads but the page is blank

- **Entry point mismatch.** The configured output directory must contain the entry file the runtime expects.
- **Build step not run.** Run the build before deploying, or put the command and its output directory in `automaton.json` so the CLI does it.
- **JavaScript error.** Open the browser console on the page and look for errors.

## The App Editor AI chat does not respond

Go to **Settings → AI Keys** and confirm that at least one valid AI provider key is saved. The editor makes no AI requests without one.

## A banned address

The server bans addresses that probe for junk paths or fail many sign-ins, for an hour, longer each time it returns. List bans with `sudo fail2ban-client status automaton-scanner` (or `sshd`, `automaton-signin`) and lift one with `sudo fail2ban-client unban <address>`. To never ban an address, see [Security overview](./security#scanners-and-password-guessing).

## Installing failed

See the common errors in [Install the server](./install#common-errors): DNS, ports, certificates, `command not found`. Rerun the same command; generated credentials are kept.

## Search server logs in the browser

Install the VictoriaLogs app from the catalog and approve its `logs:receive` permission (a system administrator can do it on the install page). Within about a minute the API, the Worker and the Runner that hosts the app send their logs to it, with no setting and no restart; they keep writing their log files too. Filter by level (`severity_text:Error`) and watch live. Pausing, uninstalling or revoking the permission stops it quietly. When a Runner cannot sync the receiver, or the server has no verified gateway host for it, the Runner or API log shows one warning (not one every 30 seconds). Only a Runner installed by Automaton (it publishes a gateway the server has verified) serves the automatic choice, and logs travel over plain `http` on the server's private network. With two such apps on different Runners, the first one to report keeps it. A Runner on another machine than the app sends nothing until you set `Logging__Otlp__Endpoint` to an `https` address of the app on that Runner (use `http` only on the same server or Docker network). The same setting on the API, the Worker or a Runner always wins over the app and turns the automatic choice off for that process; leave it empty for the automatic one. The app keeps 14 days and at most 2 GiB by default; change `RETENTION_PERIOD` or `MAX_DISK_SPACE_BYTES` in the app's settings. The Home card of the app says when nothing from Automaton arrives.

## Common questions

**Does it cost anything?** The software is free under Apache 2.0. You pay for your servers, domain and any AI services you choose.

**Do I need an AI coding agent?** No, but Automaton is designed around one. See [Work with AI](./work-with-ai).

**What can I run?** Static sites, APIs, long-running servers in containers, and finite jobs called Actions. See [Build an app](./build-an-app). Not autoscaling or multi-region failover; see [What is Automaton](./what-is-automaton).

**Can strangers sign up?** No. Self sign-up is disabled; an administrator creates or invites each user.

**Where does my data live?** On the servers you install Automaton on.

**How do updates and backups work?** [Update the server](./install#update-the-server-and-runners) and [Back up and restore](./backup-restore).

**Can I use more than one server?** Yes. Install a Runner on each machine. With several, pick one per app (`automaton set-runner`). There is no automatic balancing or failover.

**How is it secured?** [Security overview](./security).

**I found a bug or have a question.** Open an issue on [GitHub](https://github.com/rzvdaniel/automaton/issues).
