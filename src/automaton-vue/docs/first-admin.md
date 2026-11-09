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

**The email you type in step 3 is the administrator's sign-in.** Nothing on the server stores it for you. `/etc/automaton/installation.json` holds only the certificate email, which Let's Encrypt uses for expiry notices; it is not an account and signing in with it gives 401. If you forgot which email you used, check the confirmation you saved or ask whoever ran the setup; an AI agent must ask the person, not guess.

SMTP is not needed to start: the first administrator's email is confirmed by this setup flow. The setup page does not reopen once setup is complete.

## Without a browser (SSH or an AI agent)

Studio's setup page calls two endpoints, and you can call them yourself. The snippet below needs Bash (for process substitution) and `jq`, which a fresh Ubuntu 24.04 installation does not include; install it first with `sudo apt-get install -y jq`. Run this on the server, in a shell where the password is typed at a hidden prompt, never given as an argument and never echoed:

```bash
API=https://api.example.com
read -rp "Administrator email: " EMAIL
read -rp "Name: " NAME
read -rsp "Password (12+ characters, upper, lower, number, symbol): " PASSWORD; echo
# No terminal (an AI agent or a script)? Read it from a file only you can read instead:
#   PASSWORD=$(< /root/admin-password); (create it with: umask 077; read -rs p; printf %s "$p" > /root/admin-password)
# or from standard input: IFS= read -r PASSWORD
# An agent generates a compliant password into the variable, never echoed and never in a file:
#   PASSWORD=$(printf 'Aa1!'; openssl rand -base64 24 | tr -d '\n=')   # then see "When an AI agent sets up the server"

# 1. Optional: ask whether setup is open and whether a token is required.
curl -s "$API/api/install/status"

# 2. Create the first administrator. The setup token goes from the file into a header
#    through a pipe, so it is never printed or stored in your shell history.
jq -n --arg name "$NAME" --arg email "$EMAIL" --arg password "$PASSWORD" \
   '{name: $name, email: $email, password: $password}' |
  curl -s -w '\nHTTP %{http_code}\n' -X POST "$API/api/install" \
    -H @<(printf 'X-Automaton-Setup-Token: %s\n' "$(sudo cat /etc/automaton/secrets/setup-token)") \
    -H 'Content-Type: application/json' --data-binary @-
unset PASSWORD
```

`POST /api/install` takes `name`, `email` and `password` and returns 200 when the account exists. A wrong or missing token returns 403 ("A valid server setup token is required."); a password that is too weak returns 400 with the reason; 409 means setup was already completed. `POST /api/install/checks` (same header) runs the installation checks that Studio shows after the first sign-in. The token the installer printed is single-purpose: when setup completes the server replaces the content of `/etc/automaton/secrets/setup-token` with a new random value that is never printed, so the printed one stops working. Re-running the installer or the updater does not print or reset it. The new value is what lets root create [one-time password links](#when-an-ai-agent-sets-up-the-server-for-you). If you edit the token file by hand, restart the API afterwards (it is mounted into the container as one file).

`EMAIL` and `PASSWORD` in the snippet are the sign-in you are creating now: the email you chose, not the certificate email. Keep both: the CLI sign-in below asks for them again. Then sign in the CLI without a browser: see [Log in the CLI on a server without a browser](./automaton-cli#log-in-without-a-browser).

The password never goes on the command line: `jq --arg` receives it from the shell variable. If you keep it in a file, make it mode 0600 and delete it afterwards.

## When an AI agent sets up the server for you

The full agent procedure is [Install with your AI agent](./install-with-ai). An agent needs a signed-in CLI to deploy, so it creates the administrator itself with a password it generates and never prints or writes to a file (`PASSWORD=$(printf 'Aa1!'; openssl rand -base64 24 | tr -d '\n=')` in the snippet above, used in the same shell, then `unset`). It ends with a one-time link for you:

```bash
sudo "$(command -v automaton)" server password-link --email admin@example.com
```

Agent version of the snippet above (same shell as the install, `API` and `EMAIL` set as in [Install the server](./install-server#for-ai-agents)):

```bash
PASSWORD=$(printf 'Aa1!'; openssl rand -base64 24 | tr -d '
=')
jq -n --arg name "Administrator" --arg email "$EMAIL" --arg password "$PASSWORD"    '{name: $name, email: $email, password: $password}' |
  curl -s -w '
HTTP %{http_code}
' -X POST "$API/api/install"     -H @<(printf 'X-Automaton-Setup-Token: %s
' "$(cat /etc/automaton/secrets/setup-token)")     -H 'Content-Type: application/json' --data-binary @-
```

Check: the output ends with `HTTP 200`. Keep `PASSWORD` for the CLI sign-in, then `unset` it. Run the link command last, after any app you were asked to build, so the 30-minute link is fresh when the user sees it. As a non-root user, use `sudo`. Show the printed link in your final message, together with `https://studio.<domain>`. The command prints a link to Studio's set-password page. The link works once and for 30 minutes. Opening it asks for a new password; the generated one stops working at that moment and you sign in with yours. If the link expires or is lost, run the command again: each new link voids the earlier ones. Only someone who can read `/etc/automaton/secrets/setup-token` (root) can create it, it needs no sign-in and no email (SMTP), and it works only for administrator accounts. The command finds the server from `/etc/automaton/installation.json`, so it works under `sudo` without the CLI's sign-in; use `--config-dir` if the configuration is elsewhere and `--server <api-url>` to name another server. Treat the link like a password until you have used it.

## Verify it worked

Studio opens after sign-in and **Admin → Installation** shows **Check installation** results. Public DNS and open ports show as unverified there; check them from another network.

## Common errors

- **Token rejected.** Use the current value from `/etc/automaton/secrets/setup-token`. After setup is complete the printed token no longer works; the file holds a new value for root.
- **Setup returns 409.** The administrator already exists. Run `server password-link` to give the user a link.
- **Link rejected or rate limited.** Creating and using links is limited to 5 per hour per IP; wait and run the command again.
- **Password refused.** Use 12 or more characters with all four character kinds.

Next: [add a Runner](./deploy-runner).
