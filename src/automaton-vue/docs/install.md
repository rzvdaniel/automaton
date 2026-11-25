---
title: Install Automaton
description: From a fresh Linux server to a connected Runner in Studio, on one page.
order: 6
---

# Install Automaton

This page takes you from a fresh server to a Runner that shows as connected in Studio. It takes about ten minutes. Want your AI to do it? Use [Work with AI](./work-with-ai).

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

Run this on the server in Bash. It asks for your email and a password at a hidden prompt (12 or more characters with upper and lower case, a number and a symbol). The email you type is your sign-in; it is not the certificate email.

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

The last line must say `HTTP 200`. Then run `unset PASSWORD`.

## 4. Sign in the CLI

```bash
curl -fsSL https://github.com/rzvdaniel/automaton/releases/latest/download/install.sh | sh
export PATH="$HOME/.local/bin:$PATH"
automaton login --device --server "$API"
```

Open the address it prints in a browser, sign in with the administrator account and approve. `automaton whoami` should name your server. Other ways to sign in: [CLI login](./cli#log-in-without-a-browser).

## 5. Add a Runner

A Runner is the service that builds and runs your apps. Install one on the server itself:

```bash
automaton runners install --name main --local --yes
```

It waits up to five minutes and prints the Runner ID when it connects. Another machine: `automaton runners install --name NAME --ssh user@host --yes`. No sudo there: use `--print-command` and run the printed command on that host before `--timeout` (300 seconds) ends. On a Windows PC, use **Runners → Install Machine Management Runner** in Studio and follow it.

## 6. See it in Studio

```bash
automaton runners
```

`main` should be `connected`. Open `https://studio.example.com`, sign in and open **Runners**: `main` shows as online. You are done.

## Set up email

A fresh server cannot send email, so invitations and password resets reach nobody until you set it up. Sign in as a system administrator, open **Settings → Outgoing email**, enter the SMTP host, port, TLS setting, account, password and sender from your mail provider, then select **Save SMTP settings**, **Check connection** and **Send test email** to a real address.

## Update the server and Runners

You must be a system administrator.

```bash
automaton server update --wait --runners
```

It installs the latest release (pin one with `--version server-vX.Y.Z`), checks `/health` and prints the version. `--runners` also upgrades Runners on an old image; a server update alone leaves them as they are. `automaton runners upgrade main --wait` upgrades one. Check the last run with `automaton server update-status`. In Studio: **Server → Update Automaton**. Over SSH: `sudo bash /opt/automaton-server/update.sh`. A failed update leaves the old release running and names the failing step; the log is `/var/lib/automaton/update/status/update.log`. Remove a Runner with `automaton runners delete main --ssh user@host` after you delete or move its apps.

## Common errors

- **DNS check fails.** Fix the records the installer lists, wait, rerun the same command. Proxied (orange cloud) records break certificates.
- **`automaton: command not found`.** Run `export PATH="$HOME/.local/bin:$PATH"`.
- **Certificate not issued.** Port 80 must be reachable. A Let's Encrypt `429 rateLimited` means the host names used their weekly limit (five certificates in seven days): wait for the retry time it prints, then rerun the same command.
- **Port in use or another installation found.** The installer refuses to take over; free the port or use a clean server.
- **Retrying.** Rerun the same command with the same domain and email; generated credentials are kept. Never run `docker compose down -v`: it deletes data.

## Uninstall

```bash
curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/uninstall-automaton.sh -o uninstall-automaton.sh
sudo bash uninstall-automaton.sh --dry-run      # list what would go
sudo bash uninstall-automaton.sh                # remove, keep data
sudo bash uninstall-automaton.sh --delete-data --confirm example.com   # also delete data, after a final backup
```

Docker stays. Without `--delete-data` your volumes, backups and backup key are kept, and a fresh install refuses to run over them.

## For AI agents

Ask the user only for what they alone can give: root SSH access, the domain (A records for `domain` and `*.domain`, DNS only), the certificate email and the administrator's email. Run every step in one root shell. Install `jq curl python3 libicu-dev` first and check `jq --version`. Install the server with `--quiet`, which keeps the setup link out of the output. Never print or store the setup token (`/etc/automaton/secrets/setup-token`).

Create the administrator with a password the agent generates and keeps only in a shell variable, then `unset`:

```bash
PASSWORD=$(printf 'Aa1!'; openssl rand -base64 24 | tr -d '\n=')
```

Post it as in step 3 (reading the token with `cat`); the answer must end with `HTTP 200`. Sign the CLI in without a browser, add the Runner, build any requested app, and last run `automaton server password-link --email "$EMAIL"` (prefix `sudo` as non-root). Show the printed link in the final message: it works once for 30 minutes and lets the user set their own password, and each new link voids the earlier ones. Treat it like a password.

## Move a MySQL server to PostgreSQL

Servers installed before PostgreSQL (`release.env` has a `MYSQL_IMAGE` line) keep their data in MySQL until `migrate-to-postgresql.sh` copies and verifies every table and switches over. The MySQL volume is never modified. Before you start: 10 GiB free, a running server, an administrator account in `AUTOMATON_CHECK_EMAIL` / `AUTOMATON_CHECK_PASSWORD`, a provider snapshot, `tmux` or `screen`, and a quiet moment: API and Worker are down while data is copied (apps keep running). Rehearse on a restored backup on a scratch machine first (`--rehearsal`).

```bash
curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/docker/install/migrate-to-postgresql.sh -o migrate-to-postgresql.sh
less migrate-to-postgresql.sh
sudo bash migrate-to-postgresql.sh --version server-vX.Y.Z                         # report only
sudo -E bash migrate-to-postgresql.sh --version server-vX.Y.Z --run --confirm your-domain.example
```

The report lists work in flight; wait for it or pass `--discard-in-flight`. The run takes a recovery backup (`backups/mysql-to-postgresql-<time>`), copies everything in one transaction, verifies counts, hashes, foreign keys and sequences, and starts the new API and Worker. If it stops before the switch it returns to MySQL by itself (otherwise fix the problem and run `--rollback`, safe to repeat). After the switch, run `--finish` with the same release and `AUTOMATON_CHECK_*` set. To go back: `sudo bash /opt/automaton-server/migrate-to-postgresql.sh --rollback --confirm your-domain.example` (anything written to PostgreSQL after the switch is lost). An adopted installation needs a rehearsal first. Afterwards sign in, open an app and run an Action that reads a secret.

## Next

- Build your first app: [Work with AI](./work-with-ai).
- Something failed: [When something goes wrong](./when-something-goes-wrong).
- Reference: [Back up and restore](./backup-restore) and [Security overview](./security).
