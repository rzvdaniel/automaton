---
title: Server Installation
order: 1
---

# Server installation and setup

A server release installs the API, Studio, the public app store, Worker, MySQL, RabbitMQ and Traefik. The installer supports Ubuntu 24.04 or newer on x86_64 with public DNS. It installs a missing Docker Compose plugin automatically. It requires a published server release; it does not change an existing blue/green production installation.

After the installer finishes, open `/install`, enter the server setup token read locally on the host, and select **Check installation**. Studio never displays the saved token. Resolve any failing service checks, then create the first system administrator. Email delivery is optional at this stage. The setup token stops working when administrator creation commits. Sign in afterward.

System administrators can open **Admin → Installation** and select **Check installation** to inspect database/broker connections and configuration presence. **Verified** means a connection check succeeded; **configured** means a setting is present; **unverified** requires a manual or external check. Public DNS and inbound ports cannot be confirmed from inside the server. The page returns no saved secret values.

The public app store at `store.<domain>` lists apps whose owners chose to list them; it is **Public** by default. To turn it off, open **Admin → Marketplace Apps** and set **App store** to **Disabled**. The store then shows that it is turned off, and its public catalog and creator endpoints return 404. Listings are kept and reappear when you set it back to **Public**. Running apps stay reachable at their own addresses either way.

Under **Outgoing email**, enter the SMTP host, port, TLS setting, account, password, sender and support addresses, then select **Save SMTP settings**. For an existing server-file configuration, enter its password to move to Studio-managed settings. After that, leave the password blank to retain it during edits. Select **Check connection** to verify TLS and account authentication without sending a message. Saving any change clears the previous verification. Enter a test recipient and select **Send test email** only when you want to send a real message. SMTP acceptance does not prove inbox delivery. The saved password is encrypted in the database and never displayed. The server-file configuration remains the fallback until Studio settings are saved; a decryption error blocks sending instead of falling back. Back up the database and API Data Protection key ring together so restored SMTP settings remain readable. A mounted key directory is not proof of an off-host backup.

The control plane needs a separate Machine Management Runner to execute applications. Use the [Self-hosted Runner installation flow](./deployment-targets.md) to enroll a machine and assign environments explicitly. The installer does not grant a Runner trust automatically.

System administrators can review app capability requests from **Admin → Users → open a user → Apps → open an app → Permissions**. A permission appears after the app requests it in `application.permissions` in `automaton.json` and deploys. Select **Approve** to issue the app a scoped credential, **Reject** to decline a pending request, or **Revoke** to invalidate an approved credential immediately. The first supported capability is `backups:read`; its credential is delivered to the app as `APP_AUTOMATON_API_KEY` at the next Action run or runtime start. The app can use it as a bearer token for `GET /api/install/backups`. Removing the request and deploying invalidates the existing credential. App credentials cannot use other API endpoints, and they stop working while the app is hidden or disabled. Approval applies to the app, not to one build, so approve only apps whose owner you trust with that capability. `APP_AUTOMATON_API_KEY` is reserved and cannot be used as an app secret name.

To update a standalone server, run `sudo bash /opt/automaton-server/update.sh --version server-vX.Y.Z` on the host using a published release. The updater verifies the bundle, saves a protected recovery backup of the database, configuration and Data Protection keys, and checks health after updating compatible API, Worker and Studio images. Keep that backup in encrypted off-host storage. Releases that change Compose or infrastructure image digests need a separately reviewed migration. The installer itself only resumes its original release. See [server installation and updates](../../../../../docs/prod/install-automaton.md) for recovery and offline bundle instructions.

## Backups

**Admin → Installation → Backups** (system administrators) lists the server's nightly backups with their time, size and the command that restores each one. The **Backups** check under **Configuration status** is *configured* when the last backup succeeded within 36 hours, *error* when it failed or is older, and *unverified* on servers that have not received backups yet (run the latest server update). Studio cannot start a backup or a restore: both run on the server with sudo, because restoring stops the API that serves Studio.

- **Back up now:** `sudo bash /opt/automaton-server/backup.sh`
- **Restore:** copy the backup's command from Studio and run it on the server. It asks for the server domain, saves the current state as a new backup, then replaces the whole database and Data Protection keys. Everything written after the backup is lost.

Nightly backups stay on the server. Turn on the hosting provider's server backups and keep an off-server copy as described in [backups and restore](../../../../../docs/prod/install-automaton.md#backups-and-restore).

Other server configuration belongs to the host operator. Optional OAuth and billing settings are edited in the protected host configuration and applied by recreating API/Worker. Protect the configuration, database, application data and persistent cryptographic keys with encrypted off-host backups. Losing encryption keys can prevent recovery even when the database survives.
