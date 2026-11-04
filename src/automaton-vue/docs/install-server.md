---
title: Install the server
description: Install Automaton (Studio, API, app store, PostgreSQL, RabbitMQ, Traefik) on one Ubuntu server with a domain, using one command.
---

# Install the server

This page installs your own Automaton server on one Ubuntu machine. Next steps: [create the first administrator](./first-admin), then [add a Runner](./deploy-runner).

Already have a team server? Ask its administrator for the API URL and go to [Create your first app](./getting-started).

## Prerequisites

- An x86_64 Ubuntu 24.04 server with sudo access, outbound internet and at least 10 GiB free disk space.
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

3. Wait for it to finish. It installs Docker if missing, downloads the latest stable release, verifies its checksum, starts the stack and prints a one-time first-administrator setup link. Open that link in the next step.

Options: `--version server-vX.Y.Z` pins a release; `--public-ip ADDRESS` is for servers behind NAT (repeat for IPv6); `--tls-mode cloudflare` uses a Cloudflare wildcard certificate (enter the DNS token only at the hidden prompt). Never pass secrets as command arguments.

## Verify it worked

```bash
curl -fsS https://api.example.com/health
```

Expected: `ok` with HTTP 200. Then open `https://studio.example.com` and continue with [Create the first administrator](./first-admin).

## Common errors

- **DNS check fails.** The installer lists the names that do not resolve to this server. Fix the records, wait for DNS, rerun the same command. Proxied (orange cloud) records break certificates and the Runner broker.
- **Port in use or different installation found.** The installer refuses to take over. Free the port or use a clean server.
- **Certificate not issued.** Port 80 must be reachable from the internet.
- **Retrying.** Rerun the same command with the same domain and email; generated credentials are kept. Never run `docker compose down -v`: it deletes data.

For custom paths, protected settings, adopted installs and recovery, see the [operator reference](https://github.com/rzvdaniel/automaton/blob/main/docs/prod/install-automaton.md). To update or restore later: [Update the server](./update-server), [Back up and restore](./backup-restore).
