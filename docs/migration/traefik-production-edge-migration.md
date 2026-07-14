# Production Traefik Edge Migration

This runbook covers the production migration to Traefik for API, Studio, Website, and managed runner app traffic.

## Target

```text
Cloudflare -> Traefik :80/:443 -> API, Studio, Website, managed runners
```

Only Traefik should bind ports `80` and `443`.

## Cloudflare

Verify proxied DNS records for `automaton.monster`, `www`, `api`, `studio`, and `apps`.

Use SSL/TLS mode `Full (strict)`, never Flexible.

For dynamic app hostnames, use Traefik ACME DNS-01 with a Cloudflare API token restricted to the Automaton zone and only:

```text
Zone:Read
DNS:Edit
```

Create the token in Cloudflare and store it only on the production server in `.env`:

```env
CF_DNS_API_TOKEN=<cloudflare-api-token>
```

Pass it to the Traefik service in `docker-compose.prod.yml`:

```yaml
environment:
  CF_DNS_API_TOKEN: ${CF_DNS_API_TOKEN:?CF_DNS_API_TOKEN is required}
```

Configure Traefik's resolver in `.prod-runtime/traefik/traefik.yml` with DNS-01:

```yaml
certificatesResolvers:
  letsencrypt:
    acme:
      email: "__ACME_EMAIL__"
      storage: "/etc/traefik/acme/acme.json"
      dnsChallenge:
        provider: cloudflare
```

Persist `acme.json`, verify its permissions, and test certificate issuance after Traefik starts. Do not run Certbot and Traefik as competing certificate authorities for the same domains.

## Production configuration

The interactive bootstrap helper can collect and configure the values below:

```bash
./scripts/prod-bootstrap-traefik.sh
```

It explains where to obtain the Cloudflare token, reads secrets without echoing them, preserves existing root `.env` values, verifies `config/appsettings.Production.json` exists, validates Docker Compose, and can start Traefik. Review the generated `.env` before continuing. The script does not overwrite API application settings.

On the server:

```bash
cd /opt/automaton
git pull
```

Verify `/opt/automaton/.env` contains:

```env
API_DOMAIN=api.automaton.monster
STUDIO_DOMAIN=studio.automaton.monster
WEBSITE_DOMAIN=automaton.monster
APPS_DOMAIN=apps.automaton.monster
ACME_EMAIL=operations@example.com
RUNTIME_PROXY_AUTH_KEY=<long-random-secret>
```

Preserve the existing database, RabbitMQ, JWT, data-protection, email, Stripe, AI, and hosted-runner settings.

Verify `/opt/automaton/config/appsettings.Production.json` contains the production database, authentication, RabbitMQ, email, AI, and data-protection settings. Do not commit this file or secrets.

The compose file supplies the new runtime settings: `PublishedApps__RuntimeProxyAuthKey`, `PublishedApps__TraefikDynamicConfigRoot`, and the `HostedRunners__*` settings.

## Traefik persistence

Verify `.prod-runtime/traefik/traefik.yml`, `.prod-runtime/traefik/dynamic/api-edge.yml`, and `.prod-runtime/traefik/dynamic/` exist.

The API writes managed runner routes to `.prod-runtime/traefik/dynamic/managed-runtime-routes.yml`.

Persist and protect `prod-traefik-acme:/etc/traefik/acme`. Never remove the ACME, MySQL, RabbitMQ, API app-data, or runner workspace volumes during a normal deployment.

## Preflight

```bash
bash -n scripts/prod-blue-green-common.sh
bash -n scripts/prod-redeploy-all.sh
bash -n scripts/prod-verify.sh
docker compose -f docker-compose.prod.yml config >/tmp/automaton-prod-compose.yml
docker compose -f docker-compose.prod.yml ps
ss -ltnp | grep -E ':80|:443'
```

Confirm nginx is not required by the compose file or active deployment script.

## Deployment

Back up runtime state, then deploy with rollback protection:

```bash
docker compose -f docker-compose.prod.yml ps > /tmp/automaton-prod-services.before.txt
cp -a .prod-runtime "/tmp/automaton-prod-runtime-$(date +%Y%m%d%H%M%S)"
./scripts/prod-redeploy-all.sh --keep-previous
```

The script should start MySQL, RabbitMQ, Worker, and Traefik; build and warm API, Studio, and Website target colors; atomically update Traefik; and verify live origins.

## Validation

```bash
docker compose -f docker-compose.prod.yml exec -T traefik wget -q -O- http://127.0.0.1:8082/ping
curl -fsS https://api.automaton.monster/healthz
curl -I https://studio.automaton.monster/
curl -I https://automaton.monster/
curl -I https://www.automaton.monster/
curl -I http://api.automaton.monster/healthz
./scripts/prod-verify.sh
```

After deploying an app, inspect `.prod-runtime/traefik/dynamic/managed-runtime-routes.yml` and test frontend root, assets, SPA deep links, `/api/health`, uploads, SignalR/WebSockets, runner reconnect, app redeploy, and Traefik restart.

## Rollback

Keep the previous color running if validation fails. Restore the previous Traefik edge file from the runtime backup, restart Traefik, and only stop the previous colors after successful validation and the configured drain period.

## Cleanup

Once Traefik ACME is validated, remove or rewrite the old Certbot/nginx instructions and scripts. Do not run Certbot renewal alongside Traefik ACME for the same domains.
