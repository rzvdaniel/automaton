---
title: Deploy and publish an app
description: Validate, deploy and publish an Automaton app from the CLI, check its status, set a marketplace listing, and delete it.
---

# Deploy and publish an app

This page ships an app to a Runner and makes it public. Deploy and publish are separate: deploy builds and starts the app, publish gives it a public URL.

## Prerequisites

- An app folder with `automaton.json` ([Create your first app](./getting-started)).
- At least one online Runner ([Add a Runner](./deploy-runner)).

## Steps

1. Validate and deploy. The deployment is asynchronous; wait for `Succeeded` in `automaton status` (the `Deploy complete` line `deploy` prints means the package was applied, not that the app is running).

   ```bash
   automaton validate
   automaton deploy
   ```

   With one online Runner it is assigned automatically. With several, add `--runner NAME`. `automaton deploy <target-slug>` selects a remote deployment target; `--env NAME` selects a local project profile, not a remote target.

2. Check the result.

   ```bash
   automaton status
   automaton logs
   automaton apps
   ```

3. Publish when it is within what was asked for. `--yes` skips the prompt (use it for automation after approval); `--slug` sets the public URL name. Publishing waits for the TLS certificate before it reports success.

   ```bash
   automaton publish --yes --slug my-app
   ```

   The *private address* is the `app-<id>.apps...` URL that `automaton status` shows. It needs about a minute for its certificate. A static site (`--template static`) has no process behind it, so that URL answers `503` until you publish; after `automaton publish` the public URL answers.

   `automaton deploy --publish` deploys and publishes in one step.

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

A fresh server's **Install apps** tab is empty: nothing is preinstalled. A first tool app comes from the [automaton-apps repository](https://github.com/rzvdaniel/automaton-apps). For Off-site Backup: clone the repository, `cd maintenance/offsite-backup`, run `automaton create` (it creates the remote app for that folder), then `automaton deploy` and `automaton publish --install --yes --slug offsite-backup`. The app then appears under **Install apps**.

Installing a published tool app: in Studio open **Apps → Install apps**, which lists published installable apps (including those with no web page), choose one, name it and select **Install**; Studio creates the app, deploys it on a Runner and shows each step. This needs an online Runner ([Add a Runner](./deploy-runner)). The store's **Install** button opens the same page.

To let other people install a copy of your published app on their server, publish with `automaton publish --install` (`--forkable` still works). The store then shows an **Install** button; an app with no web page (Actions only) shows only **Install**, an installable web app shows **Open** and **Install**.

`automaton deploy` and `automaton publish` refuse an app that declares no runtime command, static site or Action.

Summary up to 200 characters, description up to 500. Images are PNG, JPG, WEBP or GIF up to 2 MB: one cover and up to 8 screenshots. Remove one with `automaton listing remove-screenshot <id>`. The same settings are in Studio under the app's Settings.

## Delete an app

```bash
automaton delete --yes
```

This permanently deletes the linked remote app, its runtime and its data.

## Common errors

- **No Runner available.** Deploy can upload but cannot start the app. Add or start a Runner, then deploy again.
- **Health check fails.** The app must listen on `AUTOMATON_PORT` and answer the path in `application.health.path`. Read `automaton logs`.
- **Secrets missing.** See [Secrets and permissions](./secrets-and-permissions).
- More: [Troubleshooting](./troubleshooting).
