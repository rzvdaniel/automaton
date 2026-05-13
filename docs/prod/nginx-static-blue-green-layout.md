# Nginx + Static Blue/Green + Docker Compose Production Layout

This document defines the recommended replacement for the current Traefik-based production stack.

Goal:

- keep Cloudflare permanently proxied
- keep zero-downtime style deploys for `website`, `studio`, and `api`
- remove Docker label discovery and dynamic routing surprises
- make the live routing decision an explicit file change plus `nginx` reload

This is a design for this repo's production topology. It is intentionally specific so we can implement it directly.

## Why Change

The current production stack uses:

- Traefik for TLS and hostname routing
- Docker provider discovery for `api-*`, `studio-*`, and `website-*`
- generated Traefik file-provider routes for blue/green switching

That gives us clean blue/green cutovers, but it also introduces one failure mode we have already seen in production:

- the live container is healthy
- Cloudflare reaches the origin
- Traefik temporarily claims the live Docker service "does not exist"

For public production traffic, we want something more boring:

- static upstream names
- static vhost files
- explicit blue/green switch files
- no runtime dependency on Docker label discovery

## High-Level Topology

Public traffic:

`Browser -> Cloudflare -> nginx -> chosen blue/green container`

Hostnames:

- `automaton.monster` and `www.automaton.monster` -> website
- `studio.automaton.monster` -> studio
- `api.automaton.monster` -> api

Internal Docker network:

- `nginx`
- `mysql`
- `api-blue`
- `api-green`
- `studio-blue`
- `studio-green`
- `website-blue`
- `website-green`
- optional `certbot`
- optional `website-cli-assets` build profile

Runners remain separate and unchanged.

## Main Design Choice

We keep blue/green, but the live color is no longer discovered from Docker metadata.

Instead:

- each app keeps fixed service names in Compose
- `nginx` includes small generated upstream selector files
- deploy scripts rebuild the inactive color
- deploy scripts wait for local health checks
- deploy scripts rewrite one selector file
- deploy scripts reload `nginx`
- deploy scripts optionally stop the old color

This preserves the reason blue/green was introduced in the first place:

- no moment where the public hostname points at a container that has already been stopped

## Recommended Certificate Strategy

Use `nginx + certbot` with Cloudflare always proxied and Cloudflare SSL/TLS mode set to `Full (strict)`.

Why this is the default recommendation:

- the origin has browser-valid certificates
- direct-origin tests like `curl --resolve ... 127.0.0.1` still behave normally
- no dependency on Cloudflare Origin CA for day-to-day debugging

Alternative:

- Cloudflare Origin CA also works if you never plan to use direct browser validation against the origin
- it is acceptable, but less friendly for manual troubleshooting

## Proposed File Layout

New production files:

- `docker-compose.prod.yml`
  - rewritten to use `nginx` instead of Traefik
- `docker/nginx/prod/nginx.conf`
  - main nginx config
- `docker/nginx/prod/conf.d/website.conf`
  - vhost for `automaton.monster` and `www`
- `docker/nginx/prod/conf.d/studio.conf`
  - vhost for `studio`
- `docker/nginx/prod/conf.d/api.conf`
  - vhost for `api`
- `docker/nginx/prod/upstreams/website-live.conf`
  - generated selector file
- `docker/nginx/prod/upstreams/studio-live.conf`
  - generated selector file
- `docker/nginx/prod/upstreams/api-live.conf`
  - generated selector file
- `docker/nginx/prod/ssl/`
  - mounted certificate path inside container
- `scripts/prod-nginx-blue-green-common.sh`
  - shared helpers
- `scripts/prod-nginx-switch-blue-green.sh`
  - show/change live colors
- `scripts/prod-nginx-redeploy-api.sh`
  - deploy API inactive color, switch, reload nginx
- `scripts/prod-nginx-redeploy-studio.sh`
  - same for Studio
- `scripts/prod-nginx-redeploy-website.sh`
  - same for Website
- `scripts/prod-nginx-redeploy-all.sh`
  - full stack deploy

Runtime state on the server:

- `/opt/automaton/nginx/upstreams/api-live.conf`
- `/opt/automaton/nginx/upstreams/studio-live.conf`
- `/opt/automaton/nginx/upstreams/website-live.conf`
- `/opt/automaton/nginx/state/blue-green-state.env`
- `/opt/automaton/letsencrypt/`

I recommend moving generated runtime files out of the repo tree and into `/opt/automaton/nginx/...` on the server. The repo should contain templates and scripts, not mutable live-state files.

## Docker Compose Layout

Production services:

- `nginx`
- `certbot`
- `mysql`
- `api-blue`
- `api-green`
- `studio-blue`
- `studio-green`
- `website-blue`
- `website-green`
- `website-cli-assets` profile build image

Compose network:

- `automaton-prod`

Removed from prod:

- `traefik`
- `socket-proxy`

### Compose Service Roles

`nginx`

- binds host ports `80` and `443`
- uses static vhost config
- uses selector include files to choose blue vs green
- proxies to Docker service names on `automaton-prod`
- reloads in place without dropping the entire container

`certbot`

- obtains and renews certificates
- writes certs into a shared volume or mounted host directory
- nginx reads the same cert path

`api-*`

- same ASP.NET Core app as today
- internal port `8080`
- health endpoint remains `/healthz`

`studio-*`

- same static nginx container as today
- internal port `80`

`website-*`

- same static nginx container as today
- internal port `80`

## Nginx Routing Model

Nginx should not contain hardcoded live-color choices inside the server blocks.

Instead, each vhost references a small include file that defines the current upstream target.

Example concept:

```nginx
upstream api_live {
    include /etc/nginx/upstreams/api-live.conf;
}
```

`api-live.conf` would contain exactly one live backend:

```nginx
server api-green:8080;
```

Equivalent selector files:

- `website-live.conf` -> `server website-blue:80;` or `server website-green:80;`
- `studio-live.conf` -> `server studio-blue:80;` or `server studio-green:80;`
- `api-live.conf` -> `server api-blue:8080;` or `server api-green:8080;`

This is the core predictability win:

- nginx reloads a tiny text file
- blue/green switching is no longer coupled to container label discovery

## Proposed Nginx Vhosts

### Website vhost

- `server_name automaton.monster www.automaton.monster`
- redirect HTTP to HTTPS
- terminate TLS
- proxy to `website_live`
- preserve `Host`, `X-Forwarded-*`

### Studio vhost

- `server_name studio.automaton.monster`
- redirect HTTP to HTTPS
- terminate TLS
- proxy to `studio_live`
- preserve `Host`, `X-Forwarded-*`
- websocket headers not strictly needed for the static frontend itself

### API vhost

- `server_name api.automaton.monster`
- redirect HTTP to HTTPS
- terminate TLS
- proxy to `api_live`
- preserve `Host`, `X-Forwarded-*`
- enable websocket upgrade headers for SignalR
- keep request body/timeouts reasonable for uploads and long-running requests

Recommended API-specific proxy settings:

- `proxy_http_version 1.1`
- `proxy_set_header Upgrade $http_upgrade`
- `proxy_set_header Connection $connection_upgrade`
- `proxy_read_timeout 3600`
- `proxy_send_timeout 3600`
- `client_max_body_size` set to whatever the app currently expects

## Health Check Strategy

Do not switch live traffic until the inactive color passes a local health check.

Checks:

- `api-*` -> `curl -fsS http://127.0.0.1:8080/healthz`
- `studio-*` -> `wget -q --spider http://127.0.0.1/`
- `website-*` -> `wget -q --spider http://127.0.0.1/`

Before switching, scripts should also verify from the nginx container network namespace or from the host network via container DNS:

- `curl http://api-green:8080/healthz`
- `curl -I http://studio-blue/`
- `curl -I http://website-green/`

This avoids declaring a container healthy only from its own loopback while nginx cannot reach it.

## Blue/Green State Model

State file:

- `/opt/automaton/nginx/state/blue-green-state.env`

Example:

```env
LIVE_API_COLOR=green
LIVE_STUDIO_COLOR=blue
LIVE_WEBSITE_COLOR=blue
```

The state file is the source of truth for scripts.

Selector files are derived from the state file:

- `api-live.conf`
- `studio-live.conf`
- `website-live.conf`

This mirrors the current design, but in nginx form instead of Traefik form.

## Deployment Flow

### First-time bootstrap

1. Start `mysql`.
2. Start `nginx` with temporary bootstrap upstreams that do not depend on app containers existing yet.
3. Run `certbot` to obtain certificates.
4. Start `api-blue`, `studio-blue`, and `website-blue`.
5. Run `prod-reset-blue-green.sh` to create selector files pointing live traffic to blue.
6. Reload `nginx`.
7. Keep Cloudflare proxied and set SSL mode to `Full (strict)`.

### Normal website deploy

1. Read current live color from state.
2. Resolve target color to the inactive color.
3. Build and start `website-$target`.
4. Wait for health check.
5. Update `website-live.conf` to `server website-$target:80;`
6. Reload `nginx`.
7. Verify:
   - `curl -k --resolve automaton.monster:443:127.0.0.1 https://automaton.monster -I`
   - `curl -k --resolve www.automaton.monster:443:127.0.0.1 https://www.automaton.monster -I`
8. Update state file.
9. Stop old color unless `--keep-previous`.

### Normal studio deploy

Same pattern, targeting `studio-$target`.

### Normal API deploy

Same pattern, targeting `api-$target`, but verify:

- `curl -k --resolve api.automaton.monster:443:127.0.0.1 https://api.automaton.monster/healthz`

### Full deploy

1. Ensure infrastructure (`mysql`, `nginx`, `certbot` timer/job) is up.
2. Deploy API inactive color and switch.
3. Deploy Studio inactive color and switch.
4. Deploy Website inactive color and switch.
5. Stop previous colors unless requested otherwise.

I recommend keeping service switches independent instead of forcing all three hostnames to flip to the same color together.

## Rollback Flow

Rollback should be a selector-file change, not a rebuild.

For example, to roll back Website:

1. confirm previous color still exists
2. write `website-live.conf` back to `server website-blue:80;` or `server website-green:80;`
3. reload `nginx`
4. update state file

This should complete in seconds.

## Exact Compose Design Notes

### API services

Keep the current `x-api-common` anchor almost unchanged:

- same image build
- same mounted `appsettings.Production.json`
- same SSH secret mount
- same health check

### Studio services

Keep current build args:

- `VITE_API_BASE_URL=https://${API_DOMAIN}`
- `VITE_APP_TITLE=Automaton Studio`
- `VITE_ENABLE_MOCK_API=false`
- `VITE_SUBSCRIPTIONS_ENABLED=true`

### Website services

Keep current website image and optional `website-cli-assets` profile exactly as-is.

### Shared volumes

Recommended:

- `prod-mysql-data`
- `prod-api-appdata`
- `prod-api-logs`
- `prod-nginx-certs`
- `prod-certbot-www`

Or host-mounted directories under `/opt/automaton` if you prefer explicit server filesystem visibility.

## Exact Nginx Runtime Mounts

Recommended nginx mounts:

- `/opt/automaton/nginx/nginx.conf:/etc/nginx/nginx.conf:ro`
- `/opt/automaton/nginx/conf.d:/etc/nginx/conf.d:ro`
- `/opt/automaton/nginx/upstreams:/etc/nginx/upstreams:ro`
- `/opt/automaton/letsencrypt:/etc/letsencrypt:ro`
- `/opt/automaton/certbot-www:/var/www/certbot:ro`

Port bindings:

- `80:80`
- `443:443`

## HTTP-01 vs DNS-01

With nginx, both models work.

For predictability, I recommend this order:

1. start with `certbot` using HTTP-01
2. keep Cloudflare proxied off only during the very first certificate bootstrap if needed
3. once certificates exist, keep Cloudflare proxied permanently

If you want zero Cloudflare toggling even during first bootstrap, then use DNS-01 with a Cloudflare API token.

For this repo's "boring and predictable" goal, DNS-01 is arguably the better end state even with nginx.

## Cloudflare Settings

Recommended stable settings:

- DNS records `@`, `www`, `api`, `studio` all point to the same Hetzner IP
- all are `Proxied`
- SSL/TLS mode is `Full (strict)`
- avoid `Flexible`
- purge cache only when you are debugging stale edge behavior

Optional hardening later:

- Authenticated Origin Pulls
- firewall allowlists for Cloudflare IP ranges plus admin IPs

## Outage Model Compared to Current Traefik Setup

### Current Traefik risk

- dynamic provider momentarily loses Docker service visibility
- live route can break even while the live container is healthy

### Proposed nginx static blue/green risk

- switch script writes wrong selector file
- nginx reload fails due to bad syntax
- inactive color never became healthy but script switched anyway

These risks are simpler and easier to test.

Mitigations:

- `nginx -t` before every reload
- health-check inactive color before switch
- keep old color running during deploys by default for quick rollback
- emit a clear status command that shows live colors and running containers

## Proposed Script Behavior

All production scripts should follow the same contract:

### `prod-nginx-switch-blue-green.sh`

Commands:

```bash
./scripts/prod-nginx-switch-blue-green.sh status
./scripts/prod-nginx-switch-blue-green.sh api blue
./scripts/prod-nginx-switch-blue-green.sh studio green
./scripts/prod-nginx-switch-blue-green.sh website blue
```

Behavior:

- verify target color is running
- rewrite selector file
- run `nginx -t`
- reload `nginx`
- update state file

### `prod-nginx-redeploy-api.sh`

Behavior:

- choose inactive color
- `docker compose ... up -d --build api-$target`
- wait for health
- switch selector
- verify `/healthz`
- optionally stop old color

Equivalent scripts for `studio` and `website`.

## Recommended Verification Commands

Origin checks:

```bash
curl -k --resolve api.automaton.monster:443:127.0.0.1 https://api.automaton.monster/healthz
curl -k --resolve studio.automaton.monster:443:127.0.0.1 https://studio.automaton.monster -I
curl -k --resolve automaton.monster:443:127.0.0.1 https://automaton.monster -I
curl -k --resolve www.automaton.monster:443:127.0.0.1 https://www.automaton.monster -I
```

Nginx checks:

```bash
docker compose -f docker-compose.prod.yml exec nginx nginx -t
docker compose -f docker-compose.prod.yml logs --tail 50 nginx
```

Container checks:

```bash
docker compose -f docker-compose.prod.yml ps
```

## Migration Plan From Traefik

1. Add nginx production config and scripts beside the existing Traefik setup.
2. Add an alternate production compose file or switch `docker-compose.prod.yml` in one branch.
3. Bootstrap nginx on the server while Traefik still exists, but do not bind `80/443` yet.
4. Validate:
   - nginx config
   - upstream switching
   - cert issuance
   - local `curl --resolve` checks
5. Stop Traefik.
6. Start nginx on `80/443`.
7. Re-test all public hostnames through Cloudflare.
8. Remove Traefik and socket-proxy from production after a stable period.

If you want lower-risk migration, create:

- `docker-compose.prod.traefik.yml` for the current setup
- `docker-compose.prod.nginx.yml` for the new setup

Then promote the nginx file to the primary prod compose only after validation.

## What We Keep From The Current Setup

- the existing `api`, `studio`, and `website` images
- the current blue/green color concept
- the current health checks
- the current deploy philosophy: build inactive, verify, switch, optionally stop previous

## What We Remove

- Traefik Docker provider
- Traefik file-provider route generation
- Docker socket proxy in production
- dependency on container labels for public routing

## Recommendation Summary

If the priority is predictability, the best fit for this repo is:

- `nginx`
- static vhost config
- static blue/green selector files
- Docker Compose service names as fixed upstreams
- Cloudflare permanently proxied
- `Full (strict)` SSL
- certs managed by `certbot` or DNS-01 if we want to eliminate first-issue bootstrap toggles

This preserves near-zero-downtime deploys without keeping the dynamic discovery layer that has already caused production confusion.
