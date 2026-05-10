# Automaton Production Server Setup and Initial Deployment Guide

This guide walks you through deploying the full Automaton stack on a Hetzner VPS using
Docker Compose, with Traefik as the reverse proxy and Cloudflare for DNS and TLS.

**Stack summary:**
- Hetzner VPS (Ubuntu 24.04)
- Docker + Docker Compose
- Traefik (reverse proxy + automatic TLS via Let's Encrypt)
- Cloudflare (DNS, CDN, DDoS protection)
- MySQL 8.0 (database)
- Automaton API, Studio, Website, Runner (all in Docker containers)

---

## Prerequisites

- A Hetzner account → https://www.hetzner.com
- A Cloudflare account → https://www.cloudflare.com
- A domain name managed by Cloudflare (e.g. `automaton.monster`)
- A GitHub account with access to the Automaton repository
- An SSH key pair on your local machine (see Step 0 below)

---

## Step 0 — Generate an SSH Key Pair (on your local machine)

SSH keys let you log in to the server securely without a password. You need to do this
**once on your local machine** before creating the Hetzner server.

**On Linux / macOS / Windows (PowerShell or Git Bash):**

```bash
ssh-keygen -t ed25519 -C "your-email@example.com"
```

- When asked `Enter file in which to save the key`, press **Enter** to accept the default
  location (`~/.ssh/id_ed25519`)
- When asked for a passphrase, either set one (more secure) or press **Enter** to skip

This creates two files:

| File | Description |
|------|-------------|
| `~/.ssh/id_ed25519` | **Private key** — stays on your machine, never share this |
| `~/.ssh/id_ed25519.pub` | **Public key** — this is what you give to Hetzner |

To see the public key (you'll need to paste it into Hetzner in Step 1):

```bash
cat ~/.ssh/id_ed25519.pub
```

It looks like:

```
ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAA... your-email@example.com
```

> **Why `ed25519`?** It's the modern, most secure SSH key type. Hetzner accepts it and
> it is in OpenSSH format by default. Avoid `rsa` (older, slower) unless forced to.

> **Windows note:** If you're on Windows and don't have Git Bash, open PowerShell —
> `ssh-keygen` is built into Windows 10/11.

---

## Step 1 — Create the Hetzner Server

1. Log in to Hetzner Cloud → https://console.hetzner.cloud
2. Create a new project (e.g. `Automaton`)
3. Click **Add Server** and configure:
   - **Location**: choose nearest to your users
   - **Image**: Ubuntu 24.04
   - **Type**: CPX31 (recommended) or CX22 (minimal)
   - **SSH keys**: add your public SSH key
   - **Name**: `automaton-prod`
4. Click **Create & Buy Now**
5. Note down the **public IPv4 address** of the server (e.g. `65.21.10.42`)

---

## Step 2 — Point Your Domain to Hetzner via Cloudflare

1. Log in to Cloudflare → https://dash.cloudflare.com
2. Select your domain (e.g. `automaton.monster`)
3. Go to **DNS → Records** and add the following A records:

   | Type | Name | Content | Proxy |
   |------|------|---------|-------|
   | A | `@` | `65.21.10.42` | Proxied (orange cloud) |
   | A | `www` | `65.21.10.42` | Proxied (orange cloud) |
   | A | `api` | `65.21.10.42` | Proxied (orange cloud) |
   | A | `studio` | `65.21.10.42` | Proxied (orange cloud) |

   Replace `65.21.10.42` with your actual Hetzner IP.

4. Go to **SSL/TLS → Overview** and set the encryption mode to **Full (strict)**

5. Temporarily set **all A records to DNS only (grey cloud)** — click the orange cloud icon next to each record to turn it grey.
   > This allows Let's Encrypt to reach the server directly for the HTTP-01 ACME challenge.
   > You will re-enable the proxy (orange cloud) after certs are issued (Step 15).

6. Go to **SSL/TLS → Edge Certificates** and make sure **Always Use HTTPS** is **OFF** for now.
   > We need port 80 to be reachable by Let's Encrypt when first starting Traefik.
   > You can turn it back on after the first successful deployment.

---

## Step 3 — Connect to the Server

From your local machine:

```bash
ssh root@65.21.10.42
```

Replace `65.21.10.42` with your Hetzner IP.

---

## Step 4 — Install Docker on the Server

Run all of these on the server:

```bash
# Update package list
apt update && apt upgrade -y

# Install Docker
curl -fsSL https://get.docker.com | sh

# Verify Docker is installed
docker --version
docker compose version
```

---

## Step 5 — Create a Deploy User (Recommended)

Running everything as `root` is a security risk. Create a dedicated user:

```bash
# Create user
adduser automaton

# Add to docker group so it can run docker without sudo
usermod -aG docker automaton

# Copy your SSH key to the new user
mkdir -p /home/automaton/.ssh
cp /root/.ssh/authorized_keys /home/automaton/.ssh/
chown -R automaton:automaton /home/automaton/.ssh
chmod 700 /home/automaton/.ssh
chmod 600 /home/automaton/.ssh/authorized_keys
```

Disconnect and reconnect as the automaton user:

```bash
ssh automaton@65.21.10.42
```

---

## Step 6 — Clone the Repository

The repository goes to `/opt/automaton`. This is also where Docker Compose will run from.

```bash
# Create the directory
sudo mkdir -p /opt/automaton
sudo chown automaton:automaton /opt/automaton

# Clone the repository
git clone https://github.com/rzvdaniel/automaton.git /opt/automaton

# Enter the directory
cd /opt/automaton
```

If the repository is private, you'll need to authenticate. The easiest way is a GitHub
Personal Access Token:

```bash
git clone https://YOUR_TOKEN@github.com/rzvdaniel/automaton.git /opt/automaton
```

Generate a token at: https://github.com/settings/tokens (select `repo` scope)

---

## Step 7 — Create the Config Directory

This directory holds files that are NOT in the repository (secrets):

```bash
sudo mkdir -p /opt/automaton/config
sudo chown automaton:automaton /opt/automaton/config
```

---

## Step 8 — Generate RSA Keys

The API uses RSA keys for JWT token signing. Run all commands below in order.

**1. Install the required Python package** (Ubuntu 23.04+ blocks `pip3` system-wide — use `apt`):

```bash
sudo apt install -y python3-cryptography
```

**2. Generate the keys:**

```bash
cd /opt/automaton
python3 chatgpt/GenerateRsaKeysASXml.py
```

This writes `publickey.xml` and `privatekey.xml` to `/opt/automaton/`.

**3. Inject the keys into `appsettings.Production.json`** (run after completing Step 9):

```bash
python3 - <<'EOF'
import json

config_path = "/opt/automaton/config/appsettings.Production.json"

with open("/opt/automaton/publickey.xml") as f:
    pub = f.read().strip()
with open("/opt/automaton/privatekey.xml") as f:
    priv = f.read().strip()

with open(config_path) as f:
    config = json.load(f)

if "RsaSettings" not in config["AuthenticationSettings"]:
    config["AuthenticationSettings"]["RsaSettings"] = {}

config["AuthenticationSettings"]["RsaSettings"]["RsaPrivateKey"] = priv
config["AuthenticationSettings"]["RsaSettings"]["RsaPublicKey"] = pub

with open(config_path, "w") as f:
    json.dump(config, f, indent=2)

print("Done. Keys updated.")
EOF
```

**4. Restart the API to pick up the new keys:**

```bash
docker compose -f docker-compose.prod.yml restart api-blue
docker compose -f docker-compose.prod.yml restart api-green
```

---

## Step 9 — Create `appsettings.Production.json`

This file holds all API secrets and configuration. It is mounted into the container at
runtime — it is never stored in the repository.

```bash
cp /opt/automaton/appsettings.Production.example.json \
   /opt/automaton/config/appsettings.Production.json

nano /opt/automaton/config/appsettings.Production.json
```

Fill in every value:

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://studio.automaton.monster",
      "https://automaton.monster"
    ]
  },
  "JwtOptions": {
    "ValidateIssuer": true,
    "Issuer": "https://api.automaton.monster",
    "ValidateAudience": false,
    "Audience": "Automaton.Studio",
    "SaveToken": false
  },
  "AppConfig": {
    "WebsiteUrl": "https://studio.automaton.monster",
    "DatabaseType": "MySql",
    "RefreshTokenLifetime": 4,
    "RunnerRefreshTokenLifetime": 36500,
    "UserSignUp": true,
    "AiApiEncryptionKey": "← exactly 32 random characters →"
  },
  "AuthenticationSettings": {
    "HMacSettings": {
      "SecretKey": "← at least 32 random characters →"
    },
    "RsaSettings": {
      "RsaPrivateKey": "← paste the private key XML from Step 8 →",
      "RsaPublicKey": "← paste the public key XML from Step 8 →",
      "IsIssuer": true
    }
  },
  "EmailConfig": {
    "EmailAccount": "you@yourdomain.com",
    "EmailPassword": "your-smtp-password",
    "EmailHost": "smtp.mailgun.org",
    "EmailPort": 587,
    "EmailUseSsl": true,
    "EmailSupport": "support@yourdomain.com",
    "SendEmailsInDevelopment": false
  },
  "Stripe": {
    "SecretKey": "sk_live_...",
    "PublishableKey": "pk_live_...",
    "WebhookSecret": "whsec_..."
  }
}
```

Save and close (`Ctrl+O`, `Enter`, `Ctrl+X` in nano).

> **Tip:** Generate random 32-char strings with:
> ```bash
> openssl rand -base64 32 | tr -d '=' | head -c 32
> ```

---

## Step 10 — Create the `.env` File

Docker Compose reads this file to fill in domain names and database passwords:

```bash
cp /opt/automaton/.env.prod.example /opt/automaton/.env
nano /opt/automaton/.env
```

Fill in your values:

```env
# Domains (no https://, no trailing slash)
API_DOMAIN=api.automaton.monster
STUDIO_DOMAIN=studio.automaton.monster
WEBSITE_DOMAIN=automaton.monster

# Let's Encrypt — email for certificate notifications
ACME_EMAIL=your-email@example.com

# MySQL — use strong, unique passwords
# The MySQL Docker container automatically creates the database and user
# on first startup using these values. No manual database creation needed.
MYSQL_ROOT_PASSWORD=a-very-strong-root-password
MYSQL_DATABASE=automaton
MYSQL_USER=automaton_user
MYSQL_PASSWORD=a-very-strong-user-password

```

Save and close.

> **Runners are deployed separately.** The `.env` file for the main stack does not need
> runner settings. Runners use their own `docker-compose.runner.yml` and their own `.env`
> file on whatever server they run on — see Step 14.

---

## Step 11 — Configure the Firewall

Allow only necessary ports:

```bash
# Install UFW if not present
apt install ufw -y

# Default: deny incoming, allow outgoing
ufw default deny incoming
ufw default allow outgoing

# Allow SSH (important: do this before enabling UFW or you'll lock yourself out)
ufw allow ssh

# Allow HTTP and HTTPS (required for Traefik and Let's Encrypt)
ufw allow 80
ufw allow 443

# Enable
ufw enable

# Verify
ufw status
```

---

## Step 12 — Start the Stack

```bash
cd /opt/automaton

chmod +x ./scripts/prod-*.sh
./scripts/prod-redeploy-all.sh
```

This will:
1. Generate the Traefik blue/green routing files under `docker/traefik/dynamic/`
2. Start MySQL, socket-proxy, and Traefik
3. Build and start the first live color for API, Studio, and Website
4. Point Traefik at that live color
5. Let Traefik automatically request TLS certificates from Let's Encrypt

Watch the logs to confirm everything started correctly:

```bash
docker compose -f docker-compose.prod.yml logs -f
```

Wait until you see the API say something like `Application started` or `Now listening on`.
Press `Ctrl+C` to stop following logs (containers keep running).

---

## Step 13 — Verify the Stack is Running

Check all containers are `Up`:

```bash
docker compose -f docker-compose.prod.yml ps
```

Test each URL in your browser:

- https://api.automaton.monster/swagger → API Swagger UI
- https://studio.automaton.monster → Automaton Studio login page
- https://automaton.monster → Marketing website

If you get a certificate warning, wait 1-2 minutes for Let's Encrypt to finish issuing
the certificates and refresh.

---

## Step 14 — Deploy a Runner (on any server)

Runners are independent — they only need network access to the API. You can run them on
the same server as the stack or on a completely different machine.

### 14.1 — Create a Runner API Key

1. Open https://studio.automaton.monster in your browser
2. Register the first admin account
3. Go to **API Keys**
4. Create a new key with the **Runner** scope
5. Copy the key (it starts with `ak_runner_...`)

### 14.2 — Set up the runner `.env`

On the server where you want to run the runner (can be the same or different server):

```bash
mkdir -p /opt/automaton-runner
cd /opt/automaton-runner
nano .env
```

Add:

```env
AUTOMATON_SERVER_URL=https://api.automaton.monster
AUTOMATON_API_KEY=ak_runner_your_actual_key_here
AUTOMATON_RUNNER_NAME=runner-1
```

> Set `AUTOMATON_SERVER_URL` to your public API domain. If the runner is on the same
> server as the stack, you can use `http://api:8080` and attach it to the `automaton-prod`
> Docker network instead.

### 14.3 — Start the runner

The easiest way is to pull the pre-built image from Docker Hub — no repo clone or build needed:

```bash
docker run -d \
  --name automaton-runner \
  --restart unless-stopped \
  --env-file .env \
  rzvdaniel/automaton-runner:latest
```

Alternatively, if you want to build from source, clone the repo and use Docker Compose:

```bash
git clone https://github.com/rzvdaniel/automaton.git /opt/automaton-runner
cd /opt/automaton-runner
docker compose -f docker-compose.runner.yml up -d --build
```

Verify it connected:

```bash
docker logs -f automaton-runner
# or, if using Docker Compose:
docker compose -f docker-compose.runner.yml logs -f runner
```

You should see `Connected to Automaton server` or `SignalR connection established`.

In Studio, go to **Runners** — your runner should appear as online.

> To add more runners on other servers, repeat this step on each machine with a different
> `AUTOMATON_RUNNER_NAME` and the same (or a different) API key.

---

## Step 15 — Re-enable Cloudflare Proxy and "Always Use HTTPS"

Now that the stack is verified working and Let's Encrypt certificates are issued:

1. Go to Cloudflare → your domain → **DNS**
2. Click the grey cloud icon next to each A record (`@`, `www`, `api`, `studio`) to turn it back to **orange (Proxied)**
3. Go to **SSL/TLS → Edge Certificates** and enable **Always Use HTTPS**
4. Verify the URLs still work through Cloudflare:
   - https://api.automaton.monster/
   - https://studio.automaton.monster
   - https://automaton.monster

---

## Useful Commands

```bash
# View all container statuses
docker compose -f docker-compose.prod.yml ps

# View logs for a specific service
docker compose -f docker-compose.prod.yml logs -f api-blue
docker compose -f docker-compose.prod.yml logs -f api-green
docker compose -f docker-compose.prod.yml logs -f runner

# Restart a specific service
docker compose -f docker-compose.prod.yml restart api-blue

# Pull latest code and redeploy the public stack with blue/green cutovers
cd /opt/automaton
git pull
./scripts/prod-redeploy-all.sh

# Same, but also rebuild CLI download assets for the website
./scripts/prod-redeploy-all.sh --with-cli-assets

# See which color is currently live
./scripts/prod-switch-blue-green.sh status

# Stop everything
docker compose -f docker-compose.prod.yml down

# Stop everything and delete volumes (WARNING: destroys database)
docker compose -f docker-compose.prod.yml down -v
```

### Blue/Green deploys for API, Studio, and Website

If you deploy a single service like this:

```bash
docker compose -f docker-compose.prod.yml up -d --build website
```

Compose replaces the only running container for that service. During that gap, Traefik temporarily has no backend for the hostname, and Cloudflare can hold onto the failed origin state for longer than the actual container restart.

That risk applies to `website`, `studio`, and `api`.

This guide now uses blue/green services instead:

```bash
./scripts/prod-redeploy-website.sh
./scripts/prod-redeploy-studio.sh
./scripts/prod-redeploy-api.sh
```

Or redeploy all three together:

```bash
./scripts/prod-redeploy-all.sh
```

Why this works better:

- each public service now has two dedicated Compose services: `*-blue` and `*-green`
- the deploy scripts build the inactive color, wait for health checks to pass, and only then switch Traefik to the new color
- the live color is controlled by a generated Traefik file-provider config instead of by recreating the currently live service

If you want the previous color to stay up for rollback, add `--keep-previous`:

```bash
./scripts/prod-redeploy-all.sh --keep-previous
```

You can also inspect or manually switch the live color:

```bash
./scripts/prod-switch-blue-green.sh status
./scripts/prod-switch-blue-green.sh website green
./scripts/prod-switch-blue-green.sh all blue
```

---

## Troubleshooting

### Traefik can't get a certificate

- Make sure port 80 is open in the firewall (`ufw allow 80`)
- Make sure Cloudflare "Always Use HTTPS" is OFF (temporarily)
- Make sure your DNS A records point to the correct Hetzner IP
- Check Traefik logs: `docker compose -f docker-compose.prod.yml logs traefik`

### API container exits immediately

- Check logs: `docker compose -f docker-compose.prod.yml logs api-blue` and `docker compose -f docker-compose.prod.yml logs api-green`
- Most common cause: `appsettings.Production.json` is missing or has invalid JSON
- Verify the file exists: `ls -la /opt/automaton/config/`
- Validate JSON: `python3 -m json.tool /opt/automaton/config/appsettings.Production.json`

### Can't connect to the database

- The API connection string uses `Server=mysql` (the Docker service name, not `localhost`)
- MySQL takes ~30 seconds to be ready on first start — the API will retry automatically
- Check MySQL logs: `docker compose -f docker-compose.prod.yml logs mysql`

### Runner shows as offline in Studio

- Check runner logs: `docker compose -f docker-compose.prod.yml logs runner`
- Verify the API key in `.env` is correct and has Runner scope
- Restart the runner after changing the key: `docker compose -f docker-compose.prod.yml restart runner`
