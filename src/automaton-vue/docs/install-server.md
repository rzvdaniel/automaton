---
title: Install the server
description: Install Automaton (Studio, API, app store, PostgreSQL, RabbitMQ, Traefik) on one x86_64 Linux server with a domain, using one command.
---

# Install the server

This page installs your own Automaton server on one x86_64 Linux machine. Next steps: [create the first administrator](./first-admin), then [add a Runner](./deploy-runner). Installing over SSH with no browser works too: [first administrator without a browser](./first-admin#without-a-browser-ssh-or-an-ai-agent).

Already have a team server? Ask its administrator for the API URL and go to [Create your first app](./getting-started).

Measured on 6 November 2026 on Hetzner CX23 servers (2 vCPU, 4 GB RAM, about €6.64 per month): the installer took 118 seconds on Ubuntu 22.04 and about 90 seconds on Debian 12. The Runner installation took 31–32 seconds; memory use after installation was about 1.0 GB. The Ubuntu run updated server 0.2.21 to 0.2.22 in 32 seconds; the Debian run updated 0.2.28 to 0.2.29 in 26 seconds. These are observations from the [fresh-server runs](https://github.com/rzvdaniel/automaton/issues/561), not guaranteed times or minimum requirements. A full pass after the latest fixes is still pending.

## Prerequisites

- An x86_64 Linux server with systemd, sudo access, outbound internet, at least 2 GB of RAM and 10 GiB of free disk space. Ubuntu and Debian 12 are supported (Ubuntu 24.04 and Debian 12 are the tested releases); the installer adds Docker itself there. On other distributions, [install Docker Engine and the Compose plugin](https://docs.docker.com/engine/install/) first (not tested).
- `curl` and `python3` (3.8 or newer; Debian 12's 3.11.2 works). `jq` is needed only for the headless snippets in [first administrator](./first-admin) and [CLI login](./automaton-cli#log-in-without-a-browser): `sudo apt-get install -y jq` (not preinstalled on Debian).
- A domain you can edit DNS for, and an email for certificate notices.
- Inbound TCP ports 80 and 443 (HTTPS) and 5671 (TLS Runner broker). Keep the `api` hostname directly reachable on 5671.
- DNS records pointing to the server's public IP, set to DNS only (no proxy). Pick one of:

  | Setup | Records |
  |---|---|
  | Two wildcards (shortest) | `*.example.com` and `*.apps.example.com`, plus an explicit `apps.example.com` if it does not resolve |
  | One record per name | `api`, `studio`, `store`, `apps` and `*.apps` under `example.com` |

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

## Verify it worked

```bash
curl -fsS https://api.example.com/health
```

Expected: HTTP 200 with JSON such as `{"status":"ok","version":"server-vX.Y.Z"}` where the version is the release you installed (versions before 0.2.22 return plain `ok`). Then open `https://studio.example.com` and continue with [Create the first administrator](./first-admin).

## Common errors

- **DNS check fails.** The installer lists the names that do not resolve to this server. Fix the records, wait for DNS, rerun the same command. Proxied (orange cloud) records break certificates and the Runner broker.
- **Port in use or different installation found.** The installer refuses to take over. Free the port or use a clean server.
- **Certificate not issued.** Port 80 must be reachable from the internet.
- **Retrying.** Rerun the same command with the same domain and email; generated credentials are kept. Never run `docker compose down -v`: it deletes data.

For custom paths, protected settings, adopted installs and recovery, see the [operator reference](https://github.com/rzvdaniel/automaton/blob/main/docs/prod/install-automaton.md). To update or restore later: [Update the server](./update-server), [Back up and restore](./backup-restore).
