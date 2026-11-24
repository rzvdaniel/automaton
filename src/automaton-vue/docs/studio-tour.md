---
title: A tour of Studio
description: Where to find your apps, the catalog, people, the server and backups in Studio, and what you can do on each page.
order: 6
---

# A tour of Studio

Studio is the website of your own Automaton server. Your AI builds and deploys; you use Studio to look, share and decide. The menu on the left has one page per area. Some pages, such as Backups, are for administrators.

## Apps

![The Apps page lists every app on the server with its state](/studio/apps.png)

Everything running on your server.

- **Open an app** to see its Overview, Activity and Docs, and to open the app itself.
- **Search and filter** by web apps, actions or stopped apps.
- **Pause** an app, or choose who can open it. Every app starts private.

To add one, press **New app** or **From the catalog**, or just ask your AI.

## Catalog

![The Catalog page lists apps you can install](/studio/catalog.png)

Ready-made apps you can install on this server.

- **Install** an app. Studio shows which permissions it asks for before you approve.
- **Update** an installed app when a new version is available.
- **Add a catalog** of your own, or **Install from URL**. See [Publish your own app catalog](./apps-repository).

## People

![The People page lists members and teams](/studio/people.png)

Who can use this server, and the teams they share apps in.

- **Invite people** by email, or **Create with password** when they have no email access. Sign-up is closed, so this is the only way in.
- **Search** members and see their role and how many apps they own.
- **New team** to share apps with a group.

## Server

![The Server page shows the version, health and Runners](/studio/server.png)

The state of the machine your apps run on.

- **Check the version** and read the release notes. **Check now** looks for a newer one; to upgrade, see [Update the server](./update-server).
- **Read health:** memory, disk and the nightly cleanup.
- **See Runners** on the second tab, the machines that run apps. To add one, see [Add a Runner](./deploy-runner).

## Backups

![The Backups page shows the last, next and offsite backup](/studio/backups.png)

Nightly copies of the database and the keys that decrypt it. Administrators only.

- **See** when the last backup ran, when the next one will, and whether a copy leaves the server.
- **Back up now** before a risky change.
- **Restore the whole server** from a listed backup. Studio gives you the command to run on the server; see [Back up and restore](./backup-restore).

## Also in the menu

**Home** is the start page. **Admin** and **Settings** hold server-wide and personal settings. **Documentation** opens these pages.
