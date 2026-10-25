---
title: Server Installation
order: 1
---

# Server installation and setup

A server release installs the API, Studio, Worker, MySQL, RabbitMQ and Traefik. The installer supports Ubuntu 24.04 or newer on x86_64 with public DNS. It installs a missing Docker Compose plugin automatically. It requires a published server release; it does not change an existing blue/green production installation.

After the installer finishes, open `/install`, enter the server setup token read locally on the host, and select **Check installation**. Studio never displays the saved token. Resolve any failing service checks, then create the first system administrator. Email delivery is optional at this stage. The setup token stops working when administrator creation commits. Sign in afterward.

System administrators can open **Admin → Installation** and select **Check installation** to inspect database/broker connections and configuration presence. **Verified** means a connection check succeeded; **configured** means a setting is present; **unverified** requires a manual or external check. Public DNS and inbound ports cannot be confirmed from inside the server. The page returns no saved secret values.

Under **Outgoing email**, enter the SMTP host, port, TLS setting, account, password, sender and support addresses, then select **Save SMTP settings**. For an existing server-file configuration, enter its password to move to Studio-managed settings. After that, leave the password blank to retain it during edits. Select **Check connection** to verify TLS and account authentication without sending a message. Saving any change clears the previous verification. Enter a test recipient and select **Send test email** only when you want to send a real message. SMTP acceptance does not prove inbox delivery. The saved password is encrypted in the database and never displayed. The server-file configuration remains the fallback until Studio settings are saved; a decryption error blocks sending instead of falling back. Back up the database and API Data Protection key ring together so restored SMTP settings remain readable. A mounted key directory is not proof of an off-host backup.

The control plane needs a separate Machine Management Runner to execute applications. Use the [Self-hosted Runner installation flow](./deployment-targets.md) to enroll a machine and assign environments explicitly. The installer does not grant a Runner trust automatically.

Other server configuration belongs to the host operator. Optional OAuth and billing settings are edited in the protected host configuration and applied by recreating API/Worker. Protect the configuration, database, application data and persistent cryptographic keys with encrypted off-host backups. Losing encryption keys can prevent recovery even when the database survives.
