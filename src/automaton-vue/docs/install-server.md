---
title: Install the server
description: Install Automaton (Studio, API, app store, PostgreSQL, RabbitMQ, Traefik) on one x86_64 Linux server with a domain, using one command.
---

# Install the server

This page installs your own Automaton server on one x86_64 Linux machine. **Want an AI agent to do everything, from install to a link for your password? Use [Install with your AI agent](./install-with-ai).** This page is the reference for the installer. Next steps: [create the first administrator](./first-admin), then [add a Runner](./deploy-runner).

Already have a team server? Ask its administrator for the API URL and go to [Your first app](./getting-started).

Measured times, memory and cost are in [Automaton by the numbers](./numbers): about 94 seconds for the installer on Debian 12, 2-3 minutes from the first command to a working server on Ubuntu 24.04. These are observations, not guarantees or minimum requirements.

## Prerequisites

- An x86_64 Linux server with systemd, sudo access, outbound internet, at least 2 GB of RAM and 10 GiB of free disk space. Ubuntu and Debian 12 are supported (Ubuntu 22.04, Ubuntu 24.04 and Debian 12 have been tested); the installer adds Docker itself there. On other distributions, [install Docker Engine and the Compose plugin](https://docs.docker.com/engine/install/) first (not tested).
- `curl` and `python3` (3.8 or newer). `jq` is needed only for the headless snippets in [first administrator](./first-admin) and [CLI login](./automaton-cli#log-in-without-a-browser); fresh Ubuntu and Debian lack it: `sudo apt-get install -y jq`.
- A domain you can edit DNS for, and an email for certificate notices (passed as `--email`; Let's Encrypt uses it for the HTTPS certificates; it is not an account).
- Inbound TCP ports 80 and 443 (HTTPS) and 5671 (TLS Runner broker). Keep the `api` hostname directly reachable on 5671.
- DNS A records pointing to the server's public IP, set to DNS only (no proxy). Two records are enough: `example.com` and `*.example.com`. Alternatively one record per name: `api`, `studio`, `store`, `apps` and `*.apps` under `example.com`.

Private-only, offline and ARM installs are not supported.

## Install

1. Download the installer and read it.

   ```bash
   curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/install-automaton.sh -o install-automaton.sh
   less install-automaton.sh
   ```

2. Run it with your domain and email. The installer first checks that DNS resolves to this server and stops with the records to fix if it does not.

   ```bash
   sudo bash install-automaton.sh --domain example.com --email owner@example.com
   ```

3. Wait for it to finish. It installs Docker if missing, downloads the latest stable release, verifies its checksum, starts the stack and prints a one-time first-administrator setup link. Open that link in the next step. The link contains a live setup token: do not paste the terminal output into chats, tickets or logs. To keep it out of a log or an agent's output, run the installer with output to a private file (`umask 077; sudo bash install-automaton.sh ... > ~/install.out 2>&1`), read only what you need, then delete the file. Or add `--quiet`, which stops the installer printing the link (it then points to the token file). You never need the link itself: the token is also stored in `/etc/automaton/secrets/setup-token` and [First administrator](./first-admin) reads it from there.

Options: `--version server-vX.Y.Z` pins a release; `--public-ip ADDRESS` is for servers behind NAT (repeat for IPv6); `--tls-mode cloudflare` uses a Cloudflare wildcard certificate (enter the DNS token only at the hidden prompt). Never pass secrets as command arguments.

## For AI agents

Ask the user only for what they alone can give: root SSH access, the domain (with `domain` and `*.domain` A records, DNS only) and the certificate email; also ask which email the administrator uses (default: the certificate email). Run all agent steps in one shell, as root: the generated password lives only in a shell variable and `PATH` is set for the CLI. Never print, read back or store the password. Do not paste output containing a token or link into chat.

```bash
DOMAIN=example.com
CERT_EMAIL=owner@example.com
EMAIL=$CERT_EMAIL        # administrator sign-in; use another email if the user asked
API=https://api.$DOMAIN
```

1. Install the tools. Fresh Ubuntu and Debian lack `jq`; a quiet install can fail without a message, so install visibly and check it.

   ```bash
   apt-get update && apt-get install -y jq curl python3 libicu-dev
   jq --version
   ```

   Check: `jq --version` prints a version. If not, stop and fix apt.

2. Install the server with `--quiet`, which keeps the setup link out of the output (the installer checks DNS first and installs Docker itself, about 94 s on Debian 12).

   ```bash
   curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/install-automaton.sh -o install-automaton.sh
   bash install-automaton.sh --domain "$DOMAIN" --email "$CERT_EMAIL" --quiet
   ```

   Check: `curl -fsS "$API/health"` returns HTTP 200 with `"status":"ok"`. If DNS fails, tell the user which names do not resolve, wait, and rerun the same command. Two A records were enough in every test run; if `apps.domain` itself does not resolve, add it too.

Then, in the same shell: [create the administrator](./first-admin#when-an-ai-agent-sets-up-the-server-for-you), [install the CLI](./automaton-cli#install-and-authenticate) (only the install commands; skip the plain `automaton login`) and [sign it in without a browser](./automaton-cli#log-in-without-a-browser) (after `dotnet-install.sh`, `export DOTNET_ROOT="$HOME/.dotnet" PATH="$PATH:$HOME/.dotnet:$HOME/.dotnet/tools"` in this shell), [add a Runner](./deploy-runner#for-ai-agents), build and deploy any requested app ([Build an Automaton app](./build-an-automaton-app)), and last [hand over the one-time link](./first-admin#when-an-ai-agent-sets-up-the-server-for-you). The whole path takes a few minutes.

## Verify it worked

```bash
curl -fsS https://api.example.com/health
```

Expected: HTTP 200 with JSON such as `{"status":"ok","version":"server-vX.Y.Z"}` where the version is the release you installed (versions before 0.2.22 return plain `ok`). Then open `https://studio.example.com` and continue with [Create the first administrator](./first-admin).

## Set up email

A fresh server cannot send email, so password reset emails and email invitations reach nobody until you set it up (people can still be created with a password). Studio shows system administrators a notice until email is configured. Sign in as a system administrator, open **Settings**, find **Outgoing email** on the server section, and enter the SMTP host, port, TLS setting, account, password and sender address from your mail provider. Select **Save SMTP settings**, then **Check connection**; the notice disappears once the settings are saved. A successful check proves login to the mail server, not that a message reaches an inbox, so use **Send test email** to a real address.

## Common errors

- **DNS check fails.** The installer lists the names that do not resolve to this server. Fix the records, wait for DNS, rerun the same command. Proxied (orange cloud) records break certificates and the Runner broker.
- **`automaton: command not found`.** The `export` line for `DOTNET_ROOT` and `PATH` is missing in this shell.
- **`jq: command not found`.** The agent tools step did not finish; rerun it and read its output.
- **Port in use or different installation found.** The installer refuses to take over. Free the port or use a clean server.
- **Certificate not issued.** Port 80 must be reachable from the internet. If the installer reports a Let's Encrypt `429 rateLimited` error, the same host names have used their weekly limit (five certificates in seven days; repeated installs on one domain use it up). Wait until the retry time it prints or use other host names, then rerun the same command: if HTTPS is not answering it restarts Traefik so the certificates are requested again.
- **Retrying.** Rerun the same command with the same domain and email; generated credentials are kept. Never run `docker compose down -v`: it deletes data.

For custom paths, protected settings, adopted installs and recovery, see the [operator reference](https://github.com/rzvdaniel/automaton/blob/main/docs/prod/install-automaton.md). To update or restore later: [Update the server](./update-server), [Back up and restore](./backup-restore).
