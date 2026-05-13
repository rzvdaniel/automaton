# Production Docker Cheat Sheet

This is the source of truth for the current nginx-based production stack.

Use it for both:

- first-time server bootstrap
- normal day-to-day deploys and operations

## Prerequisites

Before touching the server, make sure you have:

- a Hetzner VPS or equivalent Ubuntu host
- a Cloudflare-managed domain
- DNS records for `@`, `www`, `api`, and `studio`
- SSH access to the server
- a local SSH key pair

Recommended DNS shape in Cloudflare:

- `A @ -> server IP`
- `A www -> server IP`
- `A api -> server IP`
- `A studio -> server IP`

Recommended Cloudflare settings:

- keep all four records `Proxied`
- set SSL/TLS mode to `Full (strict)`

## Fresh Server Bootstrap

### 1. Install Docker

Run on the server:

```bash
apt update && apt upgrade -y
curl -fsSL https://get.docker.com | sh
docker --version
docker compose version
```

### 2. Clone the repo

Recommended location:

```bash
mkdir -p /opt/automaton
git clone https://github.com/rzvdaniel/automaton.git /opt/automaton
cd /opt/automaton
```

If the repo is private, clone with a GitHub token or use your normal authenticated git flow.

### 3. Create config and secret directories

```bash
mkdir -p /opt/automaton/config
mkdir -p /opt/automaton/secrets
```

### 4. Create production app settings

```bash
cp /opt/automaton/appsettings.Production.example.json /opt/automaton/config/appsettings.Production.json
```

Then edit:

```bash
nano /opt/automaton/config/appsettings.Production.json
```

Fill in the production values for:

- CORS origins
- JWT issuer
- HMAC secret
- RSA keys
- AI encryption key
- email
- Stripe
- any hosted-runner options you use

### 5. Generate RSA keys

Install the helper dependency:

```bash
apt install -y python3-cryptography
```

Generate keys:

```bash
cd /opt/automaton
python3 chatgpt/GenerateRsaKeysASXml.py
```

This writes `publickey.xml` and `privatekey.xml` into `/opt/automaton/`. Copy those values into `appsettings.Production.json`.

### 6. Create the production `.env`

```bash
cd /opt/automaton
cp .env.prod.example .env
nano .env
```

Set at least:

```env
API_DOMAIN=api.automaton.monster
STUDIO_DOMAIN=studio.automaton.monster
WEBSITE_DOMAIN=automaton.monster
ACME_EMAIL=you@example.com
MYSQL_ROOT_PASSWORD=strong-root-password
MYSQL_DATABASE=automaton
MYSQL_USER=automaton_user
MYSQL_PASSWORD=strong-user-password
```

Optional:

```env
CERT_NAME=automaton.monster
```

Use `CERT_NAME` only if you want the certificate stored under a different folder name than `WEBSITE_DOMAIN`.

### 7. Add hosted-runner SSH key if needed

If you use hosted-runner provisioning from the API, create:

```bash
chmod 700 /opt/automaton/secrets
touch /opt/automaton/secrets/hosted_runner_ssh_key
chmod 600 /opt/automaton/secrets/hosted_runner_ssh_key
```

Then put the private key contents into that file.

If you do not use hosted-runner provisioning yet, you can still create an empty placeholder file so the API bind mount resolves cleanly.

### 8. Open the firewall

If you use UFW:

```bash
apt install -y ufw
ufw allow ssh
ufw allow 80
ufw allow 443
ufw enable
ufw status
```

### 9. Make the production scripts executable

```bash
cd /opt/automaton
chmod +x ./scripts/prod-*.sh
```

### 10. Bootstrap certificates and first deploy

```bash
cd /opt/automaton
./scripts/prod-issue-certs.sh
docker compose -f docker-compose.prod.yml up -d --build api-blue studio-blue website-blue
./scripts/prod-reset-blue-green.sh
./scripts/prod-redeploy-all.sh --keep-previous
```

That does four things:

- `prod-issue-certs.sh` bootstraps nginx with a temporary self-signed cert if needed, requests the real Let's Encrypt certificate bundle, and reloads nginx
- `docker compose ... up -d --build api-blue studio-blue website-blue` creates a known all-blue baseline
- `prod-reset-blue-green.sh` points nginx state at that all-blue baseline
- `prod-redeploy-all.sh --keep-previous` exercises the normal green deployment flow while keeping blue available for rollback

### 11. Verify locally on the server

```bash
curl -k --resolve api.automaton.monster:443:127.0.0.1 https://api.automaton.monster/healthz
curl -k --resolve studio.automaton.monster:443:127.0.0.1 https://studio.automaton.monster -I
curl -k --resolve automaton.monster:443:127.0.0.1 https://automaton.monster -I
curl -k --resolve www.automaton.monster:443:127.0.0.1 https://www.automaton.monster -I
```

### 12. Verify publicly through Cloudflare

```bash
curl -I https://api.automaton.monster/healthz
curl -I https://studio.automaton.monster
curl -I https://automaton.monster
curl -I https://www.automaton.monster
```

If these succeed, the stack is live.

## Blue/Green Basics

nginx now serves `api`, `studio`, and `website` through generated upstream selector files:

- state file: `.prod-runtime/nginx/state/blue-green-state.env`
- upstream selector dir: `.prod-runtime/nginx/upstreams/`

Each public service has two Compose services:

- `api-blue` / `api-green`
- `studio-blue` / `studio-green`
- `website-blue` / `website-green`

The deploy scripts build the inactive color, wait for health checks to pass, rewrite the nginx selector for that hostname, reload nginx, and then stop the old color unless you pass `--keep-previous`.

## Full Stack

Normal redeploy:

```bash
cd /opt/automaton
git pull
./scripts/prod-redeploy-all.sh
```

Normal redeploy when website CLI downloads changed too:

```bash
cd /opt/automaton
git pull
./scripts/prod-redeploy-all.sh --with-cli-assets
```

Keep the previous color running for rollback:

```bash
cd /opt/automaton
./scripts/prod-redeploy-all.sh --keep-previous
```

## Single Service Redeploy

API:

```bash
cd /opt/automaton
./scripts/prod-redeploy-api.sh
```

Studio:

```bash
cd /opt/automaton
./scripts/prod-redeploy-studio.sh
```

Website:

```bash
cd /opt/automaton
./scripts/prod-redeploy-website.sh
```

Website with refreshed CLI downloads:

```bash
cd /opt/automaton
./scripts/prod-redeploy-website.sh --with-cli-assets
```

Force a specific target color:

```bash
cd /opt/automaton
./scripts/prod-redeploy-api.sh --color green
./scripts/prod-redeploy-studio.sh --color blue
./scripts/prod-redeploy-website.sh --color green
```

## Route Switching

Show which color is live:

```bash
cd /opt/automaton
./scripts/prod-switch-blue-green.sh status
```

Manually switch one hostname:

```bash
cd /opt/automaton
./scripts/prod-switch-blue-green.sh api green
./scripts/prod-switch-blue-green.sh studio blue
./scripts/prod-switch-blue-green.sh website green
```

Switch all three hostnames to the same color:

```bash
cd /opt/automaton
./scripts/prod-switch-blue-green.sh all blue
```

Reset routing state to a known all-blue baseline after the blue containers are already running:

```bash
cd /opt/automaton
./scripts/prod-reset-blue-green.sh
```

## First-Time Bootstrap

If the server is already prepared and you just need the minimal first deploy sequence:

```bash
cd /opt/automaton
cp .env.prod.example .env
# edit .env and /opt/automaton/config/appsettings.Production.json
chmod +x ./scripts/prod-*.sh
./scripts/prod-issue-certs.sh
docker compose -f docker-compose.prod.yml up -d --build api-blue studio-blue website-blue
./scripts/prod-reset-blue-green.sh
./scripts/prod-redeploy-all.sh --keep-previous
```

`prod-issue-certs.sh` bootstraps nginx with a temporary self-signed cert if needed, requests the real Let's Encrypt certificate bundle, and reloads nginx.
The explicit blue startup plus `prod-reset-blue-green.sh` creates a predictable routing baseline before the first regular blue/green redeploy.

## Infrastructure Services

These services do not rebuild from local source in this repo:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d mysql nginx
```

Pull newer upstream images first if needed:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml pull mysql nginx
docker compose -f docker-compose.prod.yml up -d mysql nginx
```

## TLS Certificates

Issue or refresh the live certificate bundle:

```bash
cd /opt/automaton
./scripts/prod-issue-certs.sh
```

Force a fresh cert request:

```bash
cd /opt/automaton
./scripts/prod-issue-certs.sh --force-renewal
```

Run a normal renewal check:

```bash
cd /opt/automaton
./scripts/prod-renew-certs.sh
```

## Website CLI Downloads

Rebuild the downloadable CLI bundles when `src/Automaton.Cli` or `shared/ai-guidance` changed:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml --profile build-assets build website-cli-assets
```

Then redeploy the website:

```bash
cd /opt/automaton
./scripts/prod-redeploy-website.sh --with-cli-assets
```

Rule of thumb:

- CLI code changed: rebuild `website-cli-assets`
- Shared AI guidance changed: rebuild `website-cli-assets`
- Website UI or docs changed without CLI asset changes: redeploy `website` only

## Logs

Follow one color:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml logs -f api-blue
docker compose -f docker-compose.prod.yml logs -f api-green
docker compose -f docker-compose.prod.yml logs -f studio-blue
docker compose -f docker-compose.prod.yml logs -f studio-green
docker compose -f docker-compose.prod.yml logs -f website-blue
docker compose -f docker-compose.prod.yml logs -f website-green
docker compose -f docker-compose.prod.yml logs -f nginx
```

Show the overall stack:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml ps
./scripts/prod-switch-blue-green.sh status
```

Check the resolved compose config:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml config
docker compose -f docker-compose.prod.yml --profile build-assets config
```

## Notes

- This is safer than `docker compose up -d --build website` because nginx keeps serving the currently live color until the new color passes health checks and the selector file is switched.
- Cloudflare should no longer need the DNS proxy off/on workaround in normal deploys, because the hostname never points at a half-restarted backend.
- API blue/green assumes database changes are backward-compatible for the duration of the cutover. Avoid schema changes where the old API crashes immediately after the migration is applied.
- The blue/green scripts run `docker compose ... --remove-orphans`, so old pre-migration containers like `automaton-prod-website-1` / `-2` get cleaned up automatically on the next deploy.
