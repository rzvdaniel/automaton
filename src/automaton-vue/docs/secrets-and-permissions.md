---
title: Secrets and permissions
description: Give an app its credentials with automaton secrets and request platform permissions that an administrator approves.
---

# Secrets and permissions

This page covers how an app gets credentials (secrets) and platform access (permissions). The field reference is in [automaton.json](./automaton-json#app-secrets); the security model is in the [security overview](./security).

## Prerequisites

An app folder linked to an app on your server ([Create your first app](./getting-started)).

## Give an app a secret

1. Declare it in `automaton.json` under `application.secrets`, for example `{ "name": "STRIPE_KEY", "description": "Stripe secret key" }`.
2. Set the value. It is read from standard input (or a hidden prompt), never from arguments, so it stays out of shell history and AI chat.

   ```bash
   automaton secrets set STRIPE_KEY
   automaton secrets set SHARED_KEY --project   # project vault, shared by the project's apps
   automaton secrets set MY_KEY --user          # your personal vault, for personal apps
   ```

3. Redeploy so a running app picks it up, and read it from the environment in code.

   ```bash
   automaton deploy
   ```

## Request a platform permission

Declare `application.permissions` (`backups:read` and `server:read`), deploy, then show the state:

```bash
automaton permissions status
```

A system administrator approves it in Studio (the command prints the approval link). Nothing is granted before that, and revoking stops the next call.

## Verify it worked

```bash
automaton secrets status
```

Every declared secret shows `present` and the scope that provides it. `automaton validate --strict` fails when one is missing.

## Common errors

- **Secret shows `missing`.** Set it in the right scope (`--project` needs `automaton project add-app` first).
- **Static site cannot use secrets.** Its files go to the browser. Put secrets in an API or server-rendered app.
- **Values are never shown again.** Saving the same name replaces the value.
