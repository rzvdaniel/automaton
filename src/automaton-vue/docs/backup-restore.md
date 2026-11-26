---
title: Back up and restore
description: Nightly server backups, how to take and list one, and how to restore the database and keys from a backup.
order: 100
---

# Back up and restore

This page covers the server's database and keys and the data of the apps this server's host runs. It does not cover data an app keeps outside the server, or apps on other Runner hosts.

## What to ask the user

Taking or listing a backup needs no input. Ask before a restore (it replaces the database, configuration and keys) and for the backup to restore. Never ask for or print the backup's encryption password or keys; keep archives off the chat.

## Prerequisites

SSH access with sudo on the server for the `sudo` commands below; they run on the server host, while the `automaton` commands run from your own machine, signed in to the server. Without sudo, use Studio's **Backups** page or `automaton server backup` and `automaton server backup-status` (they show times, not the folder name; the folder name `automaton-YYYYMMDDTHHMMSSZ` is listed by `backup.sh --list` or Studio's **Backups** page).

## What you get by default

The installer turns on a nightly backup between 03:00 and 04:00 server time and keeps the newest 7 in `/opt/automaton-server/backups/nightly`. Each holds a PostgreSQL dump and the keys that decrypt it. The **Backups** page in Studio shows the last, next and offsite backup, lists them, gives the restore command and has a **Back up now** button for administrators (CLI: `automaton server backup`, and `automaton server backup-status` for the next scheduled run; right after a backup it can show the old time for about 25 s).

Each backup folder holds `database.dump` (custom-format `pg_dump`), the API and Worker Data Protection keys, an `app-data` folder with one archive per app (see below), the server version and checksums, plus an encrypted `.tar.age` archive. Archives are encrypted to the public key in `/etc/automaton/backup-recipient`. The installer shows the matching private key once, at the end of the install, and does not keep it on the server (if a backup ever has to create a key without you watching, it keeps `/etc/automaton/secrets/backup-key` and Studio warns until you save and remove it): save it in a password manager at once, an archive cannot be restored without it, and someone who breaks into the server cannot decrypt your off-site copies. Studio's **Backups** page says whether the key is kept off the server. Administrators can also start a backup from Studio or the CLI; the API never runs it as root (a systemd path unit `automaton-backup-request.path` runs it).

**Nightly cleanup** runs in the Runner at 05:00 UTC by default: it removes unused Docker images and build cache older than 7 days and trims journal files above 200 MB, never volumes, images of existing containers or backups. Administrators see and change it on Studio's **Server** page.

## App data in the backup

The backup includes the data of every app on the server's own host, in the same encrypted archive (so Backup Warden carries it off-site unchanged):

| Kind of app | What is backed up |
|---|---|
| Web apps and Actions | each app's data directory and declared `storage.mounts` (`workspace/data/<app id>` in the Runner's workspace volume) |
| Compose apps | every named volume of the app's Compose project, such as `pdf-summarizer_uploads` |

Consistency: while an app's data is copied its running containers are paused (frozen, not stopped), so a database file such as SQLite is not copied half-written. The pause lasts as long as the copy takes, and apps resume by themselves; an app sees it as a brief stall. Apps that keep a database in a separate service should still be dumped by that service.

Not included: data an app keeps outside the server, Runners on other machines (protect `/opt/automaton/self-hosted-runners/<installation-id>` and the labeled volumes there yourself), `/etc/automaton`, and the server's own volumes other than the database (the keys are included separately). Keep copies off the machine they protect and test a restore on an isolated replacement. Backups taken before this was added contain no app data.

## Take or list a backup

```bash
sudo bash /opt/automaton-server/backup.sh            # back up now
sudo bash /opt/automaton-server/backup.sh --list     # list backups (as root, leave out sudo)
sudo systemctl list-timers automaton-backup.timer    # next scheduled run
```

These backups live on the same server. Also turn on your hosting provider's backups and copy `/opt/automaton-server/backups/nightly` and `/etc/automaton` to encrypted storage elsewhere.

The **Backup Warden** app is available in **Apps → Catalog**. Install and configure it with your own storage destination if you want automated encrypted copies. Keep its backup key separately so a clean server can decrypt an archive.

## Restore

Restoring replaces the whole database and keys. Everything written after that backup is lost. Runners keep their registration and apps keep running on them. App data on this host comes back as it was at backup time; deploys made after the backup are not restored, and apps created after it are unknown to the server. List names first with `sudo bash /opt/automaton-server/backup.sh --list`.

```bash
sudo bash /opt/automaton-server/restore.sh automaton-20261101T031500Z
```

It verifies checksums, asks you to type the server's domain (for automation, `--confirm example.com`), saves the current state as a new backup, stops the services, restores, starts and checks Studio. A backup from an older server version restores onto a newer one; the reverse is not supported.

### Restore one app

Deleting an app removes its data, so you can restore just that app's data from a backup without touching the database or other apps:

```bash
sudo bash /opt/automaton-server/restore.sh automaton-20261101T031500Z --app <app id or Compose project>
```

The app id is the one in Studio's URL for the app; for a Compose app use its project name (`pdf-summarizer`). If the id is not in the backup, the command lists what is. The app's containers are stopped, its data replaced with the backed-up files, and the containers started again. The app itself must already exist (redeploy it first if you deleted it); a Compose volume that no longer exists is recreated. A safety backup is taken first.

To move to a new server, install the same release there, copy a backup folder into `/opt/automaton-server/backups/nightly/`, restore it, and copy `/etc/automaton` if existing sessions and Runners must keep working.

## Restore on a clean second server from an off-site archive

Proven on a fresh server: install the same release (or newer) there, then:

1. Download the newest `.tar.age` archive from your off-site storage.
2. Decrypt it with the backup key from your password manager (saved to a file named `backup-key`; `restore.sh --archive <file> --key <file>` takes it directly) using the release's age image:

   ```bash
   AGE_IMAGE=$(grep -m1 '^AGE_IMAGE=' /opt/automaton-server/release.env | cut -d= -f2-)
   docker run --rm -v "$PWD":/data "$AGE_IMAGE" age -d -i /data/backup-key -o /data/backup.tar /data/automaton-YYYYMMDDTHHMMSSZ.tar.age
   ```
3. Extract it into the backups folder: `sudo tar -xf backup.tar -C /opt/automaton-server/backups/nightly/`. It then shows in `backup.sh --list`.
4. Restore it: `sudo bash /opt/automaton-server/restore.sh automaton-YYYYMMDDTHHMMSSZ --confirm your-domain`.

Every release is checked this way: the release install check installs the release, creates an app, takes a backup, installs a second fresh server, restores the backup there with `restore.sh --archive <file> --key <file> --confirm <domain>`, then signs in with the first server's password and finds the app and its data. A wrong key stops the restore at decryption and changes nothing.

Users, projects, Runners and secrets come back, and new deployments work. Runners installed before the backup stay attached to the old server until you re-enroll them.

## Servers installed before the key was kept off the server

Older installs still hold the private key in `/etc/automaton/secrets/backup-key`, and Studio warns about it. Once you have saved it (`sudo cat /etc/automaton/secrets/backup-key` into your password manager) and checked that you can read it back, remove it: `sudo shred -u /etc/automaton/secrets/backup-key`. Updates and nightly backups keep working: they only need `/etc/automaton/backup-recipient`, never create a new key while it exists, and keep encrypting to the same recipient. Do not delete the recipient file.

## Alerts when something is missing

The installer also sets up `automaton-watchdog.timer`, which runs on the machine itself (outside the containers) every 5 minutes, so it still works when Automaton is down. It alerts when:

- a server container is missing or stopped ("Automaton is down: api, worker missing"). If every data volume still exists, it starts them again with `docker compose up -d` and says so;
- a data volume is missing ("data volumes are missing: restore from backup"), with the exact `restore.sh` command. It never restores by itself and never starts containers then, because that would create an empty database;
- Studio does not answer;
- the last backup failed, or is more than 26 hours old.

The same problem is sent once, again every 6 hours while it lasts, and a "recovered" message follows. Set where alerts go in `/etc/automaton/alerts.json` (root only):

```json
{"webhookUrl": "https://hooks.slack.com/services/...", "emailTo": "you@example.com"}
```

The webhook can be Slack, Discord, Telegram (`https://api.telegram.org/bot<token>/sendMessage?chat_id=<id>`) or any URL that accepts a JSON `POST` with `text` and `content`. Email goes straight through the SMTP in `EmailConfig` of `appsettings.Production.json`, to `emailTo` or else `EmailConfig.AdminEmail`. Without a destination, problems only appear in `sudo journalctl -u automaton-watchdog.service`. To stop the containers for maintenance, first run `sudo systemctl stop automaton-watchdog.timer`, and start it again afterwards. Send a test message with `sudo python3 /opt/automaton-server/watchdog.py --test`.

## Verify it worked

```bash
curl -fsS https://api.example.com/health
automaton installation-status
```

Expected: `{"status":"ok"}` from `/health`, and `automaton installation-status` shows your version and counts your users and apps.

## Common errors

- **Studio shows a Backups error.** The last backup failed or is over 36 hours old. Check `sudo journalctl -u automaton-backup.service`.
- **Restore refuses.** Pick a name from `--list`; it must match a folder in `backups/nightly`.
