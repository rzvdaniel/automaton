---
title: Deploy and publish an app
description: Validate, deploy and publish an Automaton app from the CLI, check its status, set a marketplace listing, and delete it.
order: 80
---

# Deploy and publish an app

This page ships an app to a Runner and makes it public. Deploy and publish are separate: deploy builds and starts the app, publish gives it a public URL.

## What to ask the user

Ask for the app's name, whether to publish it (apps are private until published) and the public name for its address, and which Runner when several are online. Never ask for passwords, tokens or API keys; secrets go in with [`automaton secrets`](./secrets-and-permissions) from a file or hidden prompt.

## Prerequisites

- An app folder with `automaton.json` ([Build an Automaton app](./build-an-automaton-app); overview in [Your first app](./getting-started)).
- At least one online Runner ([Add a Runner](./deploy-runner)). Check: `automaton runners` shows one as `connected`, and `automaton whoami` names the right server.

## Steps

1. Validate and deploy. The deployment is asynchronous; the `Deploy complete` line means the package was applied, not that the app is running. Wait for `Running` in `automaton apps`. A static site (`--template static`) has no process: it answers only after `automaton publish` (step 3).

   ```bash
   automaton validate
   automaton deploy
   ```

   With one online Runner it is assigned automatically. With several, add `--runner NAME`. `automaton deploy <target-slug>` selects a remote deployment target; `--env NAME` selects a local project profile, not a remote target. `deploy` has no `--yes`: a bare `--yes` is read as an environment name.

2. Check the result. `automaton status` shows the linked app and publish settings, but does not show the deployment state; `automaton apps` shows it in the Status column.

   ```bash
   automaton status
   automaton logs
   automaton apps
   ```

3. Publish when it is within what was asked for. `--yes` skips the prompt (use it for automation after approval); `--slug` sets the public URL name. Publishing waits up to a minute for the TLS certificate. If none arrives, publish says the certificate request failed and why (for example Let's Encrypt's weekly limit); the server's administrator fixes that as the install guide's HTTPS troubleshooting describes, then you publish again.

   ```bash
   automaton publish --yes --slug my-app
   ```

   The *private address* is the `app-<id>.apps...` URL that `automaton status` shows. It needs about a minute for its certificate, and it needs sign-in: an anonymous `curl` gets `401`, so check a private app in a signed-in browser. A static site (`--template static`) has no process behind it, so that URL answers `503` until you publish; after `automaton publish` the public URL answers.

   Open `https://my-app.apps.<domain>` once published and check the real behavior, not only the status.

   `automaton deploy --publish` deploys and publishes in one step. It has no `--slug`: it reuses the address of an earlier publish, so the first time run `automaton publish --yes --slug NAME` and use `--publish` for later deploys.

## Verify it worked

Open the printed URL and check the app's real behavior, not only that the deployment succeeded. `automaton installation-status` shows the server version, counts and last backup.

## Marketplace listing

A published app is listed in your server's app store by default. Set how it appears:

```bash
automaton listing set --summary "A bluffing game about real words" --category Games --tags "game,multiplayer"
automaton listing cover cover.png
automaton listing add-screenshot lobby.png
automaton listing show
```

Or put `"summary"` and `"description"` at the top level of `automaton.json`: `automaton deploy` copies them to the listing while the app has none set in Studio or with `automaton listing`.

Every server lists installable apps under **Apps → Catalog**. The list is read from the official [automaton-apps repository](https://github.com/rzvdaniel/automaton-apps) (its generated `catalog.json`, in the root of the repository's `main` branch); nothing is preinstalled, so the server needs to reach `raw.githubusercontent.com` (GitHub sees the server's IP address and nothing else about it). The server keeps the last list it read, so a short GitHub outage leaves the tab working with a notice; if it never read a list, the tab says so and offers **Retry**. An entry that needs a newer server version says so instead of offering Install, and one you already installed shows **Installed** with a link to **Manage**.

**Catalog sources.** A system administrator can add more sources under **Apps → Catalog → Catalog sources**: a GitHub repository (`owner/name` or `https://github.com/owner/name`) with a `catalog.json` in the root of its `main` branch (another branch can be named). The server reads the file when you add the source and refuses a source it cannot read. Each entry shows the name of its source, and entries always install from that source's own repository. For a private repository, paste a fine-grained, read-only token (Contents access to that one repository): it is stored encrypted, used only by the server to read the catalog and to fetch an app that source lists at its listed commit, and is never shown again. Turn the official source off with its switch, or select the trash icon to remove a custom source: its entries disappear, apps already installed from it keep running. Only add sources you trust: an app is code that runs on your Runners. An installer sees the source, the pinned commit and the requested permissions before anything is created, permissions always need a system administrator's approval, and nothing updates automatically. Regular users and plain Admins cannot manage sources (the API answers 403).

To install, choose an entry and select **Install**, or paste a public GitHub address (`https://github.com/owner/name`, optionally `/tree/branch/folder`, where the branch name has no slash) under **Install from a Git address**. Studio shows the source repository, the pinned commit (the server fetches exactly that commit) and the requested permissions, then asks for the name, Runner, settings, secrets and schedules. A system administrator can approve the permissions right there. **Install** creates your own copy, deploys it on a Runner and shows each step. This needs an online Runner ([Add a Runner](./deploy-runner)). Installing the same app again makes an independent copy. Updates are never applied automatically.

From a terminal or an AI agent: `automaton catalog list` shows the entries with their ids, and `automaton catalog install <id> --name NAME --runner RUNNER --setting NAME=VALUE --secrets-file FILE --wait` installs one, saves its settings and secrets and deploys it. Secrets come only from a file, standard input (`--secrets-stdin`) or a hidden prompt, never from an argument. See the [CLI reference](./automaton-cli). An app that needs secrets stops with "Missing secret(s): NAME" (for example `catalog install heartbeat` needs `ADMIN_TOKEN`; `catalog install offsite-backup --setting S3_ENDPOINT=URL --setting S3_BUCKET=NAME --setting API_BASE_URL=URL --secrets-file FILE` takes three settings and the secrets `S3_ACCESS_KEY_ID` and `S3_SECRET_ACCESS_KEY`): the entry's page in **Apps → Catalog** and the error list them, so put each `NAME=value` in the `--secrets-file` and rerun. Some catalog apps (Flow Check, Site Links) need a `GITHUB_TOKEN` secret to install.

Installing a tool app that another user published on this server: the store's **Install** button opens the same install page.

To let other people install a copy of your published app on their server, publish with `automaton publish --install` (`--forkable` still works). The store then shows an **Install** button; an app with no web page (Actions only) shows only **Install**, an installable web app shows **Open** and **Install**.

`automaton deploy` and `automaton publish` refuse an app that declares no runtime command, static site or Action.

Summary up to 200 characters, description up to 500. Images are PNG, JPG, WEBP or GIF up to 2 MB: one cover and up to 8 screenshots. Remove one with `automaton listing remove-screenshot <id>`. The same settings are in Studio under the app's Settings.

## Delete an app

```bash
automaton delete --yes
```

This permanently deletes the linked remote app, its runtime, its data and its public address, which stops answering. Without a linked folder (for example a catalog-installed app), use `automaton apps delete <name-or-id> --yes`; it deletes any app you may delete.

## Pause and resume an app

Pause an app to stop it without deleting it, for example while a service it depends on is gone.

```bash
automaton pause                  # the linked app; or: automaton apps pause <app>
automaton resume                 # or: automaton apps resume <app>
automaton deploy --resume        # resume first, then deploy
```

Pausing stops the app on its Runner, switches off its schedules and makes its public address answer 503 with a short "This app is paused" page for browsers (a plain `curl` gets 503 JSON `Application runtime is unavailable.`). Its settings, secrets, data, publication and address are kept. Resuming starts it again and turns back on the schedules that were on before the pause. `automaton apps` shows `Paused` as the status. In Studio use **Pause** / **Resume** in the Apps list or on the app's Overview.

### Home, Needs you and pinned apps

Studio opens on **Home**. **Needs you** lists what is waiting for you: an administrator sees a missing Runner, a server or Runner update, apps asking for permissions, and optional tips (set up email, back up the server, install a recommended app such as Server Console with one click); everyone sees apps they can change that are failing. A card goes away when its task is done. Tips have a **Dismiss** button that hides them for you for good. Use **Pin to Home** in the Apps list or on an app's page to add the app to **Pinned apps**; each pinned app shows its state, last deploy, last Action run and Runner, and a click opens it. Pins and dismissed cards belong to you alone.

A deploy while the app is paused uploads the new version but keeps the app stopped and says so; use `--resume` (or resume first) to start it. You need the same rights as for deploying the app; others get 403. An API key works too: `POST /api/apps/{id}/pause` and `POST /api/apps/{id}/resume`.

Give a reason when you pause: `automaton pause --reason "Sends spam"` (up to 500 characters; the body of the pause call is `{ "reason": "…" }`). Administrators type it under **Pause app…** on the app's Admin tab. The owner and everyone who can open the app see it on the app's Overview next to **Paused** and in `automaton apps`; the public paused page never shows it. Resuming clears it.

## Common errors

- **No Runner available.** Deploy can upload but cannot start the app. Add or start a Runner, then deploy again.
- **Health check fails.** The app must listen on `AUTOMATON_PORT` and answer the path in `application.health.path`. Read `automaton logs`.
- **Secrets missing.** See [Secrets and permissions](./secrets-and-permissions).
- More: [Troubleshooting](./troubleshooting).
