---
title: Create the first administrator
description: Open the one-time setup link after installation, create the administrator account and sign in to Studio.
---

# Create the first administrator

This page creates the administrator of a freshly installed server. Self sign-up is disabled on purpose; every other user is created or invited by an administrator in Studio.

## Prerequisites

A server installed with [Install the server](./install-server) (`/health` returns HTTP 200 with `"status":"ok"`).

## Steps

1. Open the one-time setup link the installer printed. It fills in the setup token and Studio removes it from the address bar.
2. If you lost the link, open `https://studio.example.com/install` and paste the token from the server:

   ```bash
   sudo cat /etc/automaton/secrets/setup-token
   ```

3. Select **Check installation**, then enter your name, email and a password of at least 12 characters with uppercase, lowercase, a number and a symbol.
4. Sign in with that account.

SMTP is not needed to start: the first administrator's email is confirmed by this setup flow. The token stops working once setup is complete and the setup page does not reopen.

## Verify it worked

Studio opens after sign-in and **Admin → Installation** shows **Check installation** results. Public DNS and open ports show as unverified there; check them from another network.

## Common errors

- **Token rejected.** Use the current value from `/etc/automaton/secrets/setup-token`. After setup is complete the token no longer works.
- **Password refused.** Use 12 or more characters with all four character kinds.

Next: [add a Runner](./deploy-runner).
