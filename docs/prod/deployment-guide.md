# Automaton Production Deployment Guide

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

5. Go to **SSL/TLS → Edge Certificates** and make sure **Always Use HTTPS** is **OFF** for now.
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
adduser deploy

# Add to docker group so it can run docker without sudo
usermod -aG docker deploy

# Copy your SSH key to the new user
mkdir -p /home/deploy/.ssh
cp /root/.ssh/authorized_keys /home/deploy/.ssh/
chown -R deploy:deploy /home/deploy/.ssh
chmod 700 /home/deploy/.ssh
chmod 600 /home/deploy/.ssh/authorized_keys
```

Disconnect and reconnect as the deploy user:

```bash
ssh deploy@65.21.10.42
```

---

## Step 6 — Clone the Repository

The repository goes to `/opt/automaton`. This is also where Docker Compose will run from.

```bash
# Create the directory
sudo mkdir -p /opt/automaton
sudo chown deploy:deploy /opt/automaton

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
sudo chown deploy:deploy /opt/automaton/config
```

---

## Step 8 — Generate RSA Keys

The API uses RSA keys for JWT token signing. Generate them using the helper script
included in the repo:

```bash
cd /opt/automaton
python3 chatgpt/GenerateRsaKeysASXml.py
```

This prints two XML blobs: the **private key** and the **public key**. Keep them in a
safe place — you will paste them into `appsettings.Production.json` in the next step.

If Python is not installed:

```bash
apt install python3 -y
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
MYSQL_ROOT_PASSWORD=a-very-strong-root-password
MYSQL_DATABASE=automaton
MYSQL_USER=automaton_user
MYSQL_PASSWORD=a-very-strong-user-password

# Runner API key — leave empty for now, fill in after Step 12
AUTOMATON_API_KEY=
AUTOMATON_RUNNER_NAME=prod-runner
```

Save and close.

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

## Step 12 — Start the Stack (Without the Runner)

The runner needs an API key that doesn't exist yet. Start everything else first:

```bash
cd /opt/automaton

docker compose -f docker-compose.prod.yml up -d --build mysql api studio website traefik
```

This will:
1. Build Docker images for the API, Studio, and Website from source
2. Pull the Traefik and MySQL images
3. Start all containers
4. Traefik will automatically request TLS certificates from Let's Encrypt

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

## Step 14 — Create a Runner API Key

1. Open https://studio.automaton.monster in your browser
2. Register the first admin account
3. Go to **Settings → API Keys**
4. Create a new key with the **Runner** scope
5. Copy the key (it starts with `ak_runner_...`)

Now add it to the `.env` file:

```bash
nano /opt/automaton/.env
```

Set:

```env
AUTOMATON_API_KEY=ak_runner_your_actual_key_here
```

---

## Step 15 — Start the Runner

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d runner
```

Verify it connected to the API:

```bash
docker compose -f docker-compose.prod.yml logs runner
```

You should see something like `Connected to Automaton server` or `SignalR connection established`.

In Studio, go to **Runners** — your runner should appear as online.

---

## Step 16 — Re-enable Cloudflare "Always Use HTTPS"

Now that Let's Encrypt certificates are issued:

1. Go to Cloudflare → your domain → **SSL/TLS → Edge Certificates**
2. Enable **Always Use HTTPS**

---

## Useful Commands

```bash
# View all container statuses
docker compose -f docker-compose.prod.yml ps

# View logs for a specific service
docker compose -f docker-compose.prod.yml logs -f api
docker compose -f docker-compose.prod.yml logs -f runner

# Restart a specific service
docker compose -f docker-compose.prod.yml restart api

# Pull latest code and rebuild
cd /opt/automaton
git pull
docker compose -f docker-compose.prod.yml up -d --build

# Stop everything
docker compose -f docker-compose.prod.yml down

# Stop everything and delete volumes (WARNING: destroys database)
docker compose -f docker-compose.prod.yml down -v
```

---

## Troubleshooting

### Traefik can't get a certificate

- Make sure port 80 is open in the firewall (`ufw allow 80`)
- Make sure Cloudflare "Always Use HTTPS" is OFF (temporarily)
- Make sure your DNS A records point to the correct Hetzner IP
- Check Traefik logs: `docker compose -f docker-compose.prod.yml logs traefik`

### API container exits immediately

- Check logs: `docker compose -f docker-compose.prod.yml logs api`
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
