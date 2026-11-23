---
title: Install the server
description: Install Automaton (Studio, API, app store, PostgreSQL, RabbitMQ, Traefik) on one x86_64 Linux server with a domain, using one command.
order: 40
---

# Install the server

This page installs your own Automaton server on one x86_64 Linux machine. **Want an AI agent to do everything, from install to a link for your password? Use [Install with your AI agent](./install-with-ai).** This page is the reference for the installer. Next steps: [create the first administrator](./first-admin), then [add a Runner](./deploy-runner).

Already have a team server? Ask its administrator for the API URL and go to [Your first app](./getting-started).

Measured times, memory and cost are in [Automaton by the numbers](./numbers): about 94 seconds for the installer on Debian 12, 2-3 minutes from the first command to a working server on Ubuntu 24.04. These are observations, not guarantees or minimum requirements.

## Prerequisites

- An x86_64 Linux server with systemd, sudo access, outbound internet, at least 2 GB of RAM and 10 GiB of free disk space. Ubuntu and Debian 12 are supported (Ubuntu 22.04, 24.04, 26.04 and Debian 12 have been tested); the installer adds Docker itself there. On other distributions, [install Docker Engine and the Compose plugin](https://docs.docker.com/engine/install/) first (not tested).
- `curl` and `python3` (3.8 or newer). `jq` is needed only for the headless snippets in [first administrator](./first-admin) and [CLI login](./automaton-cli#log-in-without-a-browser); fresh Ubuntu and Debian lack it: `sudo apt-get install -y jq`.
- A domain you can edit DNS for, and an email for certificate notices (passed as `--email`; Let's Encrypt uses it for the HTTPS certificates; it is not an account).
- Inbound TCP ports 80 and 443 (HTTPS) and 5671 (TLS Runner broker). Keep the `api` hostname directly reachable on 5671.
- DNS A records pointing to the server's public IP, set to DNS only (no proxy). Two records are enough: `example.com` and `*.example.com`. Alternatively one record per name: `api`, `studio`, `store`, `apps` and `*.apps` under `example.com`.

Private-only, offline and ARM installs are not supported.

The installer downloads and checks the release bundle for you. If you download the bundle (`automaton-server.tar.gz`) yourself, also download `automaton-server.tar.gz.sha256` from the same release and run `sha256sum -c automaton-server.tar.gz.sha256` before you run `install.sh`.

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

Then, in the same shell: [create the administrator](./first-admin#when-an-ai-agent-sets-up-the-server-for-you), install the CLI with the install script, no .NET needed (`curl -fsSL https://github.com/rzvdaniel/automaton/releases/latest/download/install.sh | sh`, then `export PATH="$HOME/.local/bin:$PATH"` in this shell; on Windows `irm https://github.com/rzvdaniel/automaton/releases/latest/download/install.ps1 | iex`) and [sign it in without a browser](./automaton-cli#log-in-without-a-browser), [add a Runner](./deploy-runner#for-ai-agents), build and deploy any requested app ([Build an Automaton app](./build-an-automaton-app)), and last run `server password-link` and show the one-time link in your final message ([first-admin](./first-admin#when-an-ai-agent-sets-up-the-server-for-you)). Do not finish before the link is shown. The whole path takes a few minutes.

## Uninstall

```bash
curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/uninstall-automaton.sh -o uninstall-automaton.sh
sudo bash uninstall-automaton.sh --dry-run      # list what would go
sudo bash uninstall-automaton.sh                # remove, keep data
sudo bash uninstall-automaton.sh --delete-data --confirm example.com   # also delete data, after a final backup
```

It removes the server containers, the Runner from `runners install --local` with its app containers and networks, the systemd units, the fail2ban jails, `/opt/automaton-server` and `/etc/automaton`. Docker stays, and so do other containers. By default your Docker volumes, the nightly backups (`/opt/automaton-server/backups`) and the public backup key (`/etc/automaton/backup-recipient`, plus the private key on servers that still hold it) are kept and listed at the end. A fresh install refuses to run over kept volumes: add `--delete-data` to delete volumes, backups and key too. It needs `--confirm <your domain>` (or you type the domain in a terminal; `--yes` does not confirm it) and first saves a final encrypted backup (with its key, on servers that still hold it) in `/var/backups/automaton-final-<time>`, outside everything it deletes; if that backup fails nothing is deleted, unless you pass `--no-final-backup`. It removes only the install you point it at: containers of another install on the same host, their network and volumes stay. Running it again is safe. Agents: run `--delete-data --confirm <domain> --no-final-backup` before retrying a failed install (it has no data worth saving). The Runner's workflows and apps under `/var/lib/automaton` are kept too unless you pass `--delete-data`. With `--install-dir` or `--config-dir` the script refuses paths with `..` and any directory that is not the default and does not look like an Automaton install (a compose file or `update.sh`; `installation.json`). If Docker is stopped, `--delete-data` cannot remove volumes and says so.

## Verify it worked

```bash
curl -fsS https://api.example.com/health
```

Expected: HTTP 200 with `{"status":"ok"}`. The anonymous `/health` does not show the version; `automaton installation-status` does. Then open `https://studio.example.com` and continue with [Create the first administrator](./first-admin).

## Set up email

A fresh server cannot send email, so password reset emails and email invitations reach nobody until you set it up (people can still be created with a password). Studio shows system administrators a notice until email is configured. Sign in as a system administrator, open **Settings**, find **Outgoing email** on the server section, and enter the SMTP host, port, TLS setting, account, password and sender address from your mail provider. Select **Save SMTP settings**, then **Check connection**; the notice disappears once the settings are saved. A successful check proves login to the mail server, not that a message reaches an inbox, so use **Send test email** to a real address.

## Common errors

- **DNS check fails.** The installer lists the names that do not resolve to this server. Fix the records, wait for DNS, rerun the same command. Proxied (orange cloud) records break certificates and the Runner broker.
- **`automaton: command not found`.** The install script put it in `~/.local/bin`, which is not on `PATH` in this shell: run `export PATH="$HOME/.local/bin:$PATH"`.
- **`jq: command not found`.** The agent tools step did not finish; rerun it and read its output.
- **Port in use or different installation found.** The installer refuses to take over. Free the port or use a clean server.
- **Certificate not issued.** Port 80 must be reachable from the internet. If the installer reports a Let's Encrypt `429 rateLimited` error, the same host names have used their weekly limit (five certificates in seven days; repeated installs on one domain use it up). Wait until the retry time it prints or use other host names, then rerun the same command: if HTTPS is not answering it restarts Traefik so the certificates are requested again.
- **Retrying.** Rerun the same command with the same domain and email; generated credentials are kept. Never run `docker compose down -v`: it deletes data.

## Reference

**DNS and ports.** The installer checks `api`, `studio`, `store`, `apps` and a generated name under `*.apps` before starting. Keep `api` directly reachable: Runners use its TLS broker port 5671. PostgreSQL, plaintext RabbitMQ and RabbitMQ administration are not exposed. Behind NAT, pass `--public-ip`. Before you start: select the SSH key you will connect with when you create the server and check `ssh root@SERVER_IP true`; on Cloudflare set the records to DNS only; install pending OS updates first.

**fail2ban.** The installer sets up fail2ban with the jails `sshd`, `automaton-scanner` and `automaton-signin` (see [Security](./security)). The address you run the installer from in an SSH terminal session (with a real IP address) is never banned by `sshd` (remove it from the `# admin-ssh-ip:` line in `/etc/fail2ban/jail.d/automaton.conf` and rerun `fail2ban.sh` to drop it again); to allowlist another address add it to `ignoreip =` in `[DEFAULT]` of `/etc/fail2ban/jail.local` and run `sudo fail2ban-client reload`. Lift a ban with `sudo fail2ban-client unban <address>` (SSH: `sudo fail2ban-client set sshd unbanip <address>`).

**Catalog apps.** On a fresh install the first administrator and connected Runner trigger installation of two apps from the official catalog, started and unpublished: Server Keeper and VictoriaLogs. The installer approves every permission each of them asks for at that moment, so you see no pending permission requests for them (the approval is recorded as made by the installer; permissions an app asks for after a later update stay pending until you approve them). The one-button install of Server Keeper on an existing server approves what the app asks at that install the same way. With `logs:receive` approved, VictoriaLogs lets the API, Worker and Runners send their logs to it with no setting; it keeps at most 2 GiB. Only apps that work with no setup are installed this way. Home shows an "Installed for you" note you can dismiss, and a card "Your backups only live on this server" until you install the Backup Warden app. Other apps are not installed; install them from the catalog when you need them. Existing servers keep the apps they have and are not changed by an update. An app you remove is never installed again, and an app you installed yourself is left as you set it up. If the catalog lacks an app or needs a newer server, the installer keeps trying for 24 hours after the first Runner connects, then stops. To opt out, set `Catalog.InstallEssentialsOnFirstRunner` to `false` in `/etc/automaton/appsettings.Production.json` and restart the API before connecting the Runner.

**Configuration and secrets.** Release files are in `/opt/automaton-server`, private configuration in `/etc/automaton` (secret files owner-readable only; override both paths on every invocation if you use custom ones). The installer generates the database, broker, signing, encryption and setup secrets once; rerunning it keeps them and operator edits, and refuses a different domain, configuration directory or release. SMTP, GitHub OAuth and Stripe live in `appsettings.Production.json` (SMTP can instead be saved in Studio, see above); after editing, recreate the services:

```bash
cd /opt/automaton-server
sudo docker compose --env-file release.env --env-file .env -f compose.yaml up -d --force-recreate api worker
```

Back up the API Data Protection key ring with the database: Studio-managed SMTP settings and other encrypted records cannot be read without the matching keys. If secrets are lost, restore configuration and keys together; never generate replacements against an existing database.

**Switch the TLS mode.** The default issues certificates over HTTP validation (port 80 must stay reachable). With many published apps, switch to a Cloudflare wildcard certificate (token: Zone Read and DNS Edit for the zone) to avoid Let's Encrypt's 50 certificates per domain per week; the domain and email cannot be changed:

```bash
sudo bash /opt/automaton-server/install.sh --switch-tls --tls-mode cloudflare   # hidden token prompt, or --cloudflare-token-file PATH
sudo bash /opt/automaton-server/install.sh --switch-tls --tls-mode http         # back
```

It restarts Traefik and API, waits for the HTTPS checks, never touches credentials or data, and is safe to repeat. It refuses a hand-written `traefik.yml` (adopted installation).

**Wildcard certificates without a Cloudflare token (`delegated`).** For another DNS host, or Cloudflare as a plain DNS host with no token on the server, use `--tls-mode delegated`. It is optional; the default install is unchanged. You add **one** DNS record, once:

| Type | Name | Value |
| --- | --- | --- |
| `NS` | `_acme-challenge.apps.<domain>` | `api.<domain>` |

`api.<domain>` already has an address record. The record hands only the Let's Encrypt challenge name to a small DNS responder on your server (Compose service `acme-responder`), which answers that one name and refuses everything else (no recursion), limits queries per source address, and exists only in this mode. Traefik gets and renews one wildcard certificate for `*.apps.<domain>` through it, with no token to store.

```bash
sudo bash /opt/automaton-server/install.sh --switch-tls --tls-mode delegated   # or --tls-mode delegated on a fresh install
```

The command checks that UDP and TCP port 53 can be bound, starts the responder, then checks through public DNS, and by querying the responder over UDP and TCP at `api.<domain>`, that the record and port work. If a step fails it restores the previous TLS configuration (a failed HTTPS check afterwards keeps the new mode so you can run the same command again). **Open inbound UDP and TCP port 53** (also in a cloud security group); it is opened only in this mode. If another service already listens on port 53 (for example `systemd-resolved` on all addresses), free it first. The responder runs from a `python:3-alpine` image pinned by digest in the release (`RESPONDER_IMAGE` in `release.env`), as an unprivileged user. Switch back with `--tls-mode http` or `cloudflare`; the responder is then removed and port 53 is no longer used.

**Apps' own domains.** The owner of a public app adds a domain on the app's Domain tab. Studio shows a TXT record (`_automaton-challenge.<domain>`, proves ownership) and a CNAME to the apps domain or an A record to this server; Check DNS reports which step failed. Traefik then requests a certificate over HTTP-01. `PublishedApps:AllowedCustomDomains` limits the allowed domains; domains under the server's apps domain are refused.

**Adopted installations.** A reviewed migration can adopt an existing server into this layout (the installer does not do it). A root-owned `compose.override.yaml` beside `compose.yaml` keeps external volumes, networks and pinned infrastructure images; the updater applies it to the installed and candidate release and preserves it. Include `-f compose.override.yaml` in manual Compose commands and review the effective configuration with `docker compose ... config` (its output can contain secrets). Never attach an existing database volume to a newer database image.

**Status and recovery.**

```bash
sudo bash /opt/automaton-server/install.sh --status
cd /opt/automaton-server
sudo docker compose --env-file release.env --env-file .env -f compose.yaml logs --tail 100 api worker traefik
```

Redact logs before sharing. To retry after a failed HTTPS setup, correct the DNS records (or the Cloudflare token) and rerun the same command; for a certificate that was not issued, restart Traefik: `docker compose --project-directory /opt/automaton-server --env-file /opt/automaton-server/release.env --env-file /opt/automaton-server/.env -f /opt/automaton-server/compose.yaml restart traefik` (add `-f compose.override.yaml` on adopted servers).

To update or restore later: [Update the server](./update-server), [Back up and restore](./backup-restore).
