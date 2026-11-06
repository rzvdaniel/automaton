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

## Without a browser (SSH or an AI agent)

Studio's setup page calls two endpoints, and you can call them yourself. Run this on the server (or anywhere that can reach the API) in a shell where the password is typed at a hidden prompt, never given as an argument and never echoed:

```bash
API=https://api.example.com
read -rp "Administrator email: " EMAIL
read -rp "Name: " NAME
read -rsp "Password (12+ characters, upper, lower, number, symbol): " PASSWORD; echo

# 1. Optional: ask whether setup is open and whether a token is required.
curl -s "$API/api/install/status"

# 2. Create the first administrator. The setup token goes from the file into a header
#    through a pipe, so it is never printed or stored in your shell history.
jq -n --arg name "$NAME" --arg email "$EMAIL" --arg password "$PASSWORD" \
   '{name: $name, email: $email, password: $password}' |
  curl -s -w '\nHTTP %{http_code}\n' -X POST "$API/api/install" \
    -H @<(printf 'X-Automaton-Setup-Token: %s\n' "$(sudo cat /etc/automaton/secrets/setup-token)") \
    -H 'Content-Type: application/json' --data-binary @-
```

`POST /api/install` takes `name`, `email` and `password` and returns 200 when the account exists. A wrong or missing token returns 403 ("A valid server setup token is required."); a password that is too weak returns 400 with the reason. `POST /api/install/checks` (same header) runs the installation checks that Studio shows after the first sign-in. The token stops working once setup is complete.

Then sign in the CLI without a browser: see [Log in the CLI on a server without a browser](./automaton-cli#log-in-without-a-browser).

## Verify it worked

Studio opens after sign-in and **Admin → Installation** shows **Check installation** results. Public DNS and open ports show as unverified there; check them from another network.

## Common errors

- **Token rejected.** Use the current value from `/etc/automaton/secrets/setup-token`. After setup is complete the token no longer works.
- **Password refused.** Use 12 or more characters with all four character kinds.

Next: [add a Runner](./deploy-runner).
