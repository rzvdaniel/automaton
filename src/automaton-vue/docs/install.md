---
title: Install Automaton
description: From a fresh Linux server to a connected Runner in Studio, on one page.
order: 6
---

# Install Automaton

This page takes you from a fresh server to a Runner that shows as connected in Studio. It takes about ten minutes. Want your AI to do it? Use [Install with your AI agent](./install-with-ai).

## You need

- One x86_64 Linux server (Ubuntu 22.04, 24.04, 26.04 or Debian 12) with at least 2 GB of RAM, 10 GiB of free disk, `sudo` and internet access.
- A domain whose DNS you can edit, and an email address for certificate notices.
- Ports 80, 443 and 5671 open to the internet.
- `curl`, `python3` and `jq`. On a fresh server: `sudo apt-get install -y curl python3 jq`.

## 1. Point your domain at the server

Create two A records for the server's public IP, set to **DNS only** (no proxy): `example.com` and `*.example.com`. Use your own domain.

## 2. Install the server

```bash
curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/install-automaton.sh -o install-automaton.sh
less install-automaton.sh
sudo bash install-automaton.sh --domain example.com --email you@example.com --quiet
```

The installer checks DNS first and tells you which names to fix. It installs Docker if needed and starts the server. `--quiet` keeps the one-time setup link out of the terminal; you read the token from a file in step 3.

Check it:

```bash
curl -fsS https://api.example.com/health
```

You should see `"status":"ok"`.

## 3. Create the first administrator

Run this on the server. It asks for your email and a password at a hidden prompt (12 or more characters with upper and lower case, a number and a symbol). The email you type is your sign-in; it is not the certificate email.

```bash
API=https://api.example.com
read -rp "Administrator email: " EMAIL
read -rsp "Password: " PASSWORD; echo
jq -n --arg name "Administrator" --arg email "$EMAIL" --arg password "$PASSWORD" \
   '{name: $name, email: $email, password: $password}' |
  curl -s -w '\nHTTP %{http_code}\n' -X POST "$API/api/install" \
    -H @<(printf 'X-Automaton-Setup-Token: %s\n' "$(sudo cat /etc/automaton/secrets/setup-token)") \
    -H 'Content-Type: application/json' --data-binary @-
```

The last line must say `HTTP 200`. Keep `EMAIL` and `PASSWORD` in this shell for the next step.

## 4. Sign in the CLI

```bash
curl -fsSL https://github.com/rzvdaniel/automaton/releases/latest/download/install.sh | sh
export PATH="$HOME/.local/bin:$PATH"
automaton login --device --server "$API"
```

Open the address it prints in a browser, sign in with the administrator account and approve. Then `unset PASSWORD`. `automaton whoami` should name your server. Other ways to sign in: [CLI login](./automaton-cli#log-in-without-a-browser).

## 5. Add a Runner

A Runner is the service that builds and runs your apps. Install one on the server itself:

```bash
automaton runners install --name main --local --yes
```

It waits up to five minutes and prints the Runner ID when it connects. Another machine, or a Windows PC: [Add a Runner](./deploy-runner).

## 6. See it in Studio

```bash
automaton runners
```

`main` should be `connected`. Open `https://studio.example.com`, sign in and open **Runners**: `main` shows as online. You are done.

## Next

- Build your first app: [Work with AI](./work-with-ai).
- Email for invitations and password resets: [Set up email](./install-server#set-up-email).
- Something failed: [Troubleshooting](./troubleshooting), or the common errors in [Install the server](./install-server#common-errors).
- Later: [update](./update-server), [back up](./backup-restore), [uninstall](./install-server#uninstall).

The full reference pages stay available: [Install the server](./install-server), [Create the first administrator](./first-admin), [Add a Runner](./deploy-runner).
