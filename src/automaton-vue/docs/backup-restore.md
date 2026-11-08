---
title: Back up and restore
description: Nightly server backups, how to take and list one, and how to restore the database and keys from a backup.
---

# Back up and restore

This page covers the server's database and keys. It does not cover files that apps keep in their own data directories.

## Prerequisites

SSH access with sudo on the server.

## What you get by default

The installer turns on a nightly backup between 03:00 and 04:00 server time and keeps the newest 7 in `/opt/automaton-server/backups/nightly`. Each holds a PostgreSQL dump and the keys that decrypt it. the **Backups** page in Studio shows the last, next and offsite backup, lists them and gives the restore command.

## Take or list a backup

```bash
sudo bash /opt/automaton-server/backup.sh            # back up now
sudo bash /opt/automaton-server/backup.sh --list     # list backups
sudo systemctl list-timers automaton-backup.timer    # next scheduled run
```

These backups live on the same server. Also turn on your hosting provider's backups and copy `/opt/automaton-server/backups/nightly` and `/etc/automaton` to encrypted storage elsewhere.

The **Off-site Backup** app is available in **Apps → Catalog**. Install and configure it with your own storage destination if you want automated encrypted copies. Keep its backup key separately so a clean server can decrypt an archive.

## Restore

Restoring replaces the whole database and keys. Everything written after that backup is lost.

```bash
sudo bash /opt/automaton-server/restore.sh automaton-20261101T031500Z
```

It verifies checksums, asks you to type the server's domain (for automation, `--confirm example.com`), saves the current state as a new backup, stops the services, restores, starts and checks Studio. A backup from an older server version restores onto a newer one; the reverse is not supported.

To move to a new server, install the same release there, copy a backup folder into `/opt/automaton-server/backups/nightly/`, restore it, and copy `/etc/automaton` if existing sessions and Runners must keep working.

## Restore on a clean second server from an off-site archive

Proven on a fresh server: install the same release (or newer) there, then:

1. Download the newest `.tar.age` archive from your off-site storage.
2. Decrypt it with the backup key (`/etc/automaton/secrets/backup-key` on the old server, which you saved separately) using the release's age image:

   ```bash
   AGE_IMAGE=$(grep -m1 '^AGE_IMAGE=' /opt/automaton-server/release.env | cut -d= -f2-)
   docker run --rm -v "$PWD":/data "$AGE_IMAGE" age -d -i /data/backup-key -o /data/backup.tar /data/automaton-YYYYMMDDTHHMMSSZ.tar.age
   ```
3. Extract it into the backups folder: `sudo tar -xf backup.tar -C /opt/automaton-server/backups/nightly/`. It then shows in `backup.sh --list`.
4. Restore it: `sudo bash /opt/automaton-server/restore.sh automaton-YYYYMMDDTHHMMSSZ --confirm your-domain`.

Users, projects, Runners and secrets come back, and new deployments work. Runners installed before the backup stay attached to the old server until you re-enroll them.

## Verify it worked

```bash
curl -fsS https://api.example.com/health
automaton installation-status
```

Expected: `ok`, and your users and apps are counted.

## Common errors

- **Studio shows a Backups error.** The last backup failed or is over 36 hours old. Check `sudo journalctl -u automaton-backup.service`.
- **Restore refuses.** Pick a name from `--list`; it must match a folder in `backups/nightly`.

Details: [operator reference](https://github.com/rzvdaniel/automaton/blob/main/docs/prod/install-automaton.md#backups-and-restore).
