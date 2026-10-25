---
title: Server Installation
order: 1
---

# Server installation and setup

A server release installs the API, Studio, Worker, MySQL, RabbitMQ and Traefik. The initial installer targets Ubuntu 24.04 on x86_64 with public DNS. It requires a published server release; it does not change an existing blue/green production installation.

After the installer finishes, open `/install`, enter the server setup token, and select **Check installation**. Resolve any failing service checks, then create the first system administrator. Email delivery is optional at this stage. The setup token stops working when administrator creation commits. Sign in afterward.

System administrators can open **Admin → Installation** and select **Check installation** to inspect database/broker connections and configuration presence. The page returns no saved secret values. SMTP presence is not a delivery test, and a mounted key directory is not proof of an off-host backup.

The control plane needs a separate Machine Management Runner to execute applications. Use the [Self-hosted Runner installation flow](./deployment-targets.md) to enroll a machine and assign environments explicitly. The installer does not grant a Runner trust automatically.

Server configuration belongs to the host operator. Optional email/OAuth/billing settings are edited in the protected host configuration and applied by recreating API/Worker. Protect the configuration, database, application data and persistent cryptographic keys with encrypted off-host backups. Losing encryption keys can prevent recovery even when the database survives.
