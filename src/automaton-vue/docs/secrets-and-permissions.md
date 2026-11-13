---
title: Secrets and permissions
description: Give an app its credentials with automaton secrets and request platform permissions that an administrator approves.
---

# Secrets and permissions

This page covers how an app gets credentials (secrets) and platform access (permissions). The field reference is in [automaton.json](./automaton-json#app-secrets); the security model is in the [security overview](./security).

## Prerequisites

An app folder linked to an app on your server ([Your first app](./getting-started)).

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

Declare `application.permissions` (`backups:read`, `server:read`, `server:cleanup` and `notify:email`), deploy, then show the state:

```bash
automaton permissions status
```

A system administrator approves it in Studio (the command prints the approval link). Nothing is granted before that, and revoking stops the next call.

### Email people with `notify:email`

With `notify:email` approved, the app can ask the server to email through the server's own SMTP (Admin > Settings); it never sees SMTP credentials. Call it with the app's key:

```bash
curl -X POST "$AUTOMATON_API_URL/api/notify/email" \
  -H "Authorization: Bearer $APP_AUTOMATON_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"to":["administrators"],"subject":"Disk almost full","text":"Only 2 GB left."}'
```

`to` takes `owner` (the app's owners), `administrators`, `project` (the members of the app's project) and addresses an administrator put on the server's allowed list (`PUT /api/admin/installation/notify-recipients`). Any other address gets 403; apps cannot email arbitrary addresses, so an app that needs that brings its own SMTP or uses a webhook. The subject is prefixed with `[app name]`, the text is plain text (up to 10,000 characters), and each recipient gets their own message. An app can send 10 emails per hour (429 with `Retry-After` beyond that). Answers: 200 `{"sent":n}`, 400 invalid request, 403 not approved or address not allowed, 404 no recipient resolved, 409 SMTP not configured. Every send and refusal appears in Admin > Audit Events (subject and counts, never the text).

## Verify it worked

```bash
automaton secrets status
```

Every declared secret shows `present` and the scope that provides it. `automaton validate --strict` fails when one is missing.

## Common errors

- **Secret shows `missing`.** Set it in the right scope (`--project` needs `automaton project add-app` first).
- **Static site cannot use secrets.** Its files go to the browser. Put secrets in an API or server-rendered app.
- **Values are never shown again.** Saving the same name replaces the value.
