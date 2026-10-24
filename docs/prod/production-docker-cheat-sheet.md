# Production Docker Cheat Sheet

This is the source of truth for the current Traefik-based production stack.

Use it for both:

- one-time production setup
- normal day-to-day deploys and operations

## Prerequisites

Before touching the server, make sure you have:

- a Hetzner VPS or equivalent Ubuntu host
- a Cloudflare-managed domain
- DNS records for `@`, `www`, `api`, `studio`, and `apps`
- SSH access to the server
- a local SSH key pair

Recommended DNS shape in Cloudflare:

- `A @ -> server IP`
- `A www -> server IP`
- `A api -> server IP`
- `A studio -> server IP`
- `A apps -> server IP`

Recommended Cloudflare settings:

- keep all five records `Proxied`
- set SSL/TLS mode to `Full (strict)`

## One-Time Production Setup

### 1. Install Docker

Run on the server:

```bash
apt update && apt upgrade -y
curl -fsSL https://get.docker.com | sh
docker --version
docker compose version
```

### 1.1 Configure Docker log rotation

Create or edit the Docker daemon config:

```bash
sudo nano /etc/docker/daemon.json
```

Put this inside:

```json
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  },
  "builder": {
    "gc": {
      "enabled": true,
      "defaultKeepStorage": "2GB"
    }
  }
}
```

Validate the JSON before restart:

```bash
sudo dockerd --validate --config-file=/etc/docker/daemon.json
```

Restart Docker:

```bash
sudo systemctl restart docker
```

Check that containers came back:

```bash
docker ps
```

Important notes:

- merge these keys into the existing daemon config; preserve storage roots, networks and other settings (see [example](../../docker/host-maintenance/daemon.json.example))
- the GC keys above configure the default Docker-driver builder; run `docker buildx version`, `docker buildx ls` and `docker buildx inspect` first. Source-deployment scripts require the standard Buildx plugin and a local Docker/docker-container driver. If it is absent, the legacy builder's cache is not governed by these BuildKit settings; install the distribution's standard plugin and verify the selected builder before building. For a docker-container builder, configure that builder's BuildKit daemon instead. Do not create another builder as maintenance
- logging defaults apply to newly created containers
- existing containers can keep their old logging config until recreated
- for the blue/green `api`, `studio`, and `website` services, the next recreated containers pick this up automatically during normal deploys

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
APPS_DOMAIN=apps.automaton.monster
ACME_EMAIL=you@example.com
CF_DNS_API_TOKEN=<zone-scoped-cloudflare-dns-edit-token>
MYSQL_ROOT_PASSWORD=strong-root-password
MYSQL_DATABASE=automaton
MYSQL_USER=automaton_user
MYSQL_PASSWORD=strong-user-password
RABBITMQ_USER=automaton
RABBITMQ_PASSWORD=strong-rabbitmq-password
```

API and Runner management commands, results, and transfers use durable RabbitMQ operations and authenticated HTTPS. API blue/green deployment does not wait for Runner registration or a SignalR drain acknowledgement. After cutover, the script asks the active API color to publish durable reconnect commands on the existing Runner management queues for all eligible registered protocol-v5 Runners, including disconnected Runners and those already recorded on the target color while earlier commands are queued. Registrations with inactive bound credentials are excluded because revocation removes their queues; legacy owner-key BYO registrations remain eligible. This works when `--keep-previous` leaves an older API image running. All connected eligible Runners must support protocol version 5; otherwise the active API reports failure and the script restores the previous API route. Runners finish active tunnel requests when possible and reconnect when idle, with a 20-second maximum grace. Tunnel requests may briefly return 503 during reconnect, and active requests can be interrupted at the bound. If forward publication fails partway, the script restores the prior route and then queues compensation commands through the still-running new API to all eligible registered protocol-v5 Runners, including those currently disconnected. Runners retain the latest target until its API color handshakes, accepting either current `ApiInstanceIdentity` arguments or the retained older API's `WelcomeRunner` identity object. A handshake from a superseded color keeps tunnel admission blocked until the latest target confirms its identity. If compensation cannot be published, both API colors remain running for retry; the restored older API does not need the new endpoint. Hosted app traffic continues through Traefik directly to Runner runtime endpoints.

Use `APPS_DOMAIN` for public published apps. Public app URLs use the nested wildcard hostname
`https://{slug}.apps.automaton.monster`; the DNS provider must therefore contain a
`*.apps.automaton.monster` record pointing at the production edge. Traefik terminates TLS
and routes each published slug host to the managed runner runtime endpoint written to
`managed-runtime-routes.yml`. The base `apps` host remains the API-backed runtime entry
point and is not a blue/green application service. Per-router ACME issuance is currently
not used for managed app routes. Traefik requests one DNS-01 wildcard certificate covering
both `apps.automaton.monster` and `*.apps.automaton.monster`, then dynamic app routers use
`tls: {}` without an app-specific certificate lifecycle.

Managed runtime volumes:

- `prod-static-runtime`
  - API-owned runtime storage used for legacy/private application fallback paths
- `prod-traefik-dynamic`
  - API writes managed runner route files for private and published app hosts
  - Traefik routes frontend and backend requests directly to hosted runner endpoints

Hosted managed runners should expose a stable runtime endpoint. For same-host Docker this can be the runner container DNS name on the shared runtime network, for example `http://runner-name:8080`.

For a hosted runner on a separate server, the API allocates a port from
`HostedRunners:RemoteRuntimePortStart` through
`HostedRunners:RemoteRuntimePortEnd`, publishes that port to the runner's
runtime port, and stores an endpoint such as `http://<runner-host-address>:20000`.
Open the configured range on each remote runner host, restricted to the API or
proxy network that needs to reach the runner runtime.

### 7. Open the firewall

If you use UFW:

```bash
apt install -y ufw
ufw allow ssh
ufw allow 80
ufw allow 443
ufw enable
ufw status
```

### 9. Production scripts

The `scripts/prod-*.sh` files are tracked with their executable bit, so a fresh
checkout can run them directly. No post-checkout `chmod` step is required.

Documented host-local state (`.env`, dated `.env` snapshots, the key XML files,
the hosted-runner key files under `secrets/`, `venv/`, and `backups/`) is
intentionally ignored by Git and must be preserved across checkout updates.

### 10. Start Traefik and perform the first deploy

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d traefik
docker compose -f docker-compose.prod.yml up -d --build api-blue studio-blue website-blue
./scripts/prod-reset-blue-green.sh
./scripts/prod-redeploy-all.sh --keep-previous
```

That does four things:

- Traefik uses `CF_DNS_API_TOKEN` and `ACME_EMAIL` to request and renew the app wildcard through Cloudflare DNS-01
- `docker compose ... up -d --build api-blue studio-blue website-blue` creates a known all-blue baseline
- `prod-reset-blue-green.sh` points blue/green state at that all-blue baseline
- `prod-redeploy-all.sh --keep-previous` exercises the normal green deployment flow while keeping blue available for rollback

### 11. Verify locally on the server

```bash
curl -k --resolve api.automaton.monster:443:127.0.0.1 https://api.automaton.monster/healthz
curl -k --resolve studio.automaton.monster:443:127.0.0.1 https://studio.automaton.monster -I
curl -k --resolve apps.automaton.monster:443:127.0.0.1 https://apps.automaton.monster/runtime/automaton-api.js -I
curl -k --resolve automaton.monster:443:127.0.0.1 https://automaton.monster -I
curl -k --resolve www.automaton.monster:443:127.0.0.1 https://www.automaton.monster -I
```

### 12. Verify publicly through Cloudflare

```bash
curl -I https://api.automaton.monster/healthz
curl -I https://studio.automaton.monster
curl -I https://apps.automaton.monster/runtime/automaton-api.js
curl -I https://automaton.monster
curl -I https://www.automaton.monster
```

If these succeed, the stack is live.

For the scripted verification path, use:

```bash
cd /opt/automaton
./scripts/prod-verify.sh
```

## Blue/Green Basics

Traefik serves `api`, `studio`, `website`, and managed app traffic through generated dynamic configuration:

- state file: `.prod-runtime/nginx/state/blue-green-state.env` (blue/green state)
- Traefik dynamic dir: `.prod-runtime/traefik/dynamic/`

Each public service has two Compose services:

- `api-blue` / `api-green`
- `studio-blue` / `studio-green`
- `website-blue` / `website-green`

The deploy scripts build the inactive color, wait for health checks to pass, verify Traefik can reach that new color over the Docker network, rewrite the Traefik selector for that hostname, and then stop the old color unless you pass `--keep-previous`.

For `prod-redeploy-all.sh`, the three public selectors are now switched together and Traefik reloads its dynamic configuration once. This reduces Cloudflare origin churn during full-stack deploys.

`website` and `studio` are static frontend containers behind Traefik. API blue/green state is tracked separately; switching API colors does not wait for Runner registration.

`apps.automaton.monster` is different. It is the stable runtime edge, backed by the API for platform runtime assets and by Traefik dynamic routes for managed applications. It does not have `apps-blue` or `apps-green` containers.

Runner management work does not depend on the old API SignalR connection. Presence and heartbeat continue over SignalR. Supported BYO runtime tunnels still require the serving API instance to own the Runner connection. After cutover, a durable command from the active API prompts the Runner to reconnect even if `--keep-previous` leaves the old API image alive. The Runner drains current tunnel requests when possible, then reconnects within the 20-second grace; requests can briefly fail during the handoff. If command publication fails, the script restores the previous route before sending compensation commands through the still-running new API; if that publication also fails, keep both colors running and retry reconciliation.

## Full Stack

The following Git commands apply to the production checkout at `/opt/automaton`, not to local development checkouts. Local development may use feature branches normally. Production Git authentication belongs to the `automaton` service user. Configure the production remote to fetch only `main` if required for that checkout. Run network Git operations without sudo, then use sudo only for the local fast-forward merge when the checkout requires elevated filesystem permissions. If operating from another machine, run the same commands through SSH (for example, `ssh automaton "git -C /opt/automaton fetch origin"`).

Normal redeploy:

```bash
cd /opt/automaton
gh auth status
git fetch origin
git merge-base --is-ancestor HEAD origin/main
sudo git merge --ff-only origin/main
./scripts/prod-redeploy-all.sh
```

Normal redeploy when website CLI downloads changed too:

```bash
cd /opt/automaton
gh auth status
git fetch origin
git merge-base --is-ancestor HEAD origin/main
sudo git merge --ff-only origin/main
./scripts/prod-redeploy-all.sh
```

Keep the previous color running for rollback:

```bash
cd /opt/automaton
./scripts/prod-redeploy-all.sh --keep-previous
```

Use `--keep-previous` for normal production deploys while the deployment is still fresh. It leaves the old live color running after Traefik switches to the new color, so rollback is only a route switch.

Important for runners:

- if you use `--keep-previous`, the old API remains available for route rollback while a durable command through the active API prompts connected Runners to reconnect to the new live API
- management commands continue through RabbitMQ and authenticated HTTPS across the API color change
- supported BYO runtime tunnels can briefly return 503 during the reconnect handoff; an active request may be interrupted after the 20-second grace
- without `--keep-previous`, the old API color is stopped after the configured overlap, prompting its connected Runners to reconnect through `api.automaton.monster`

Run without `--keep-previous` only when you are comfortable removing the old color after the switch. That keeps the server cleaner and uses fewer resources, but rollback then requires rebuilding or redeploying the previous version.

## Published App Host

Public published apps are served from:

```text
https://{slug}.apps.automaton.monster/
```

The internal app-ID route remains available at `https://apps.automaton.monster/a/{appId}/`.

The app host is the stable Traefik runtime edge, not a blue/green application container. The ASP.NET API remains the control plane:

- publish validates permissions and slug ownership
- publish refreshes the hosted runner route for the public slug
- publish generates runtime metadata and ensures the app manifest exists
- unpublish removes the public slug route
- runtime app calls stay on the app host and are forwarded by Traefik to the hosted runner

Production control-plane storage shape:

```text
prod-api-appdata       -> API-owned source/live app workspace data
prod-published-apps   -> legacy/static projection retained for compatibility; managed app traffic uses runner routes
```

The active Compose stack does not require an nginx app-host service. The API writes the
Traefik dynamic route file through `prod-api-appdata`/`.prod-runtime/traefik/dynamic`,
and Traefik reads it from `/etc/traefik/dynamic`.

Normal user app publishing does not run a server-provisioning script. The API refreshes the
Traefik route file, and Traefik serves the app through its hosted runner endpoint.

Cache expectations for Cloudflare/Traefik and hosted runner assets:

```text
/{slug}/index.html           revalidate
/{slug}/manifest.webmanifest short cache
/{slug}/automaton-config.js  revalidate
/{slug}/assets/*             long immutable cache when filenames are hashed
/runtime/automaton-api.js    short cache
```

Private and unpublished apps are routed through their managed runner endpoint. The API refreshes
Traefik routes for `/a/{appId}/...` and published slug hosts; the runner serves the frontend
and backend runtime files.

Managed runtime frontend and backend traffic share the same Traefik route:

- Traefik matches `/a/{appId}/...` and published slug hosts
- Traefik forwards both frontend and `/api/...` requests to the assigned hosted runner
- Automaton.Api remains the control-plane authority and route-file writer

Restricted runtime proxy contract:

- `/a/{appId}/...` is the browser-visible runtime path, so the `automaton_runtime` cookie is scoped to `/a/{appId}`.
- `/internal/runtime/auth` will not receive that cookie automatically because the request path differs.
- The runtime proxy must forward the original browser `Cookie` header explicitly when calling `/internal/runtime/auth`.
- The runtime proxy must also send:
  - `X-App-Id: {appId}`
  - `X-Automaton-Runtime-Auth: {PublishedApps:RuntimeProxyAuthKey}`
- The API rejects `/internal/runtime/auth` requests that do not include the configured shared header value.

Production nginx shape:

```nginx
location ~ ^/a/(?<runtime_app_id>[0-9a-fA-F-]{36})/api(?:/|$) {
    auth_request /internal/runtime/auth;
    proxy_pass http://traefik:80;
}

location ~ ^/a/(?<runtime_app_id>[0-9a-fA-F-]{36})/$ {
    auth_request /internal/runtime/auth;
    root /var/www/automaton-private-runtime;
    try_files /apps/$runtime_app_id/index.html =404;
}

location ~ ^/a/(?<runtime_app_id>[0-9a-fA-F-]{36})/(?<runtime_asset_path>.+)$ {
    auth_request /internal/runtime/auth;
    root /var/www/automaton-private-runtime;
    try_files /apps/$runtime_app_id/$runtime_asset_path /apps/$runtime_app_id/index.html =404;
}

location = /internal/runtime/auth {
    internal;
    proxy_pass http://api_upstream/internal/runtime/auth;
    proxy_pass_request_body off;
    proxy_set_header Content-Length "";
    proxy_set_header X-App-Id $runtime_app_id;
    proxy_set_header X-Automaton-Runtime-Auth $runtime_proxy_auth_key;
    proxy_set_header Cookie $http_cookie;
}
```

## CI/CD Deploy Flow

A production deploy job should SSH into the server and run a small, boring sequence:

```bash
cd /opt/automaton
gh auth status
git fetch origin
git log --oneline HEAD..origin/main
git diff --stat HEAD..origin/main
git diff --name-status HEAD..origin/main
git merge-base --is-ancestor HEAD origin/main
sudo git merge --ff-only origin/main
./scripts/prod-redeploy-all.sh --keep-previous
./scripts/prod-verify.sh
```

Use `--keep-previous` by default in CI/CD. It leaves the previous color running, so rollback remains a route switch while you watch the new deployment.

Deploy verification behavior:

- local origin checks with `curl --resolve ... 127.0.0.1` are the authoritative cutover gate
- public hostname checks through Cloudflare are advisory warnings after local origin already passed
- if Cloudflare is briefly unhealthy while the local origin is healthy, the deploy should warn instead of treating the app cutover itself as failed

For a website-only deploy:

```bash
cd /opt/automaton
gh auth status
git fetch origin
git merge-base --is-ancestor HEAD origin/main
sudo git merge --ff-only origin/main
./scripts/prod-redeploy-website.sh --keep-previous
./scripts/prod-verify.sh
```

For API-only or Studio-only deploys, use `prod-redeploy-api.sh --keep-previous` or `prod-redeploy-studio.sh --keep-previous` with the same verify step.

`prod-redeploy-all.sh` also refreshes the Traefik bootstrap config and `/runtime/automaton-api.js`. Single-service deploys do not need to touch the app runtime edge unless that SDK or route bootstrap config changed.

If only the app-host runtime SDK or Traefik runtime-router bootstrap config changed, use the dedicated app-host infrastructure script:

```bash
cd /opt/automaton
gh auth status
git fetch origin
git merge-base --is-ancestor HEAD origin/main
sudo git merge --ff-only origin/main
./scripts/prod-redeploy-app-host.sh
./scripts/prod-verify.sh
```

That script copies `runtime/automaton-api.js` into `.prod-runtime` and refreshes the runtime-edge configuration. It does not rebuild API, Studio, Website, or managed runner containers.

## Cold Reinstall Checklist

Use this when reinstalling Automaton websites on a new server.

1. Point Cloudflare DNS records `@`, `www`, `api`, `studio`, and `apps` at the new server IP.
2. Install Docker and Docker Compose.
3. Clone the repository to `/opt/automaton`.
4. Create `/opt/automaton/config/appsettings.Production.json`.
5. Create `/opt/automaton/.env` from `.env.prod.example`.
6. Open firewall ports `22`, `80`, and `443`.
7. Configure `CF_DNS_API_TOKEN` and `ACME_EMAIL` in `.env`.
8. Start Traefik with `docker compose -f docker-compose.prod.yml up -d traefik`; it creates or renews the wildcard certificate in the persistent ACME volume.
9. Run `docker compose -f docker-compose.prod.yml up -d --build api-blue studio-blue website-blue`.
10. Run `./scripts/prod-reset-blue-green.sh`.
11. Run `./scripts/prod-redeploy-all.sh --keep-previous`.
12. Run `./scripts/prod-verify.sh`.

If migrating an existing production instance, copy `prod-api-appdata` and the database backup as the source of truth. `prod-published-apps` is generated output and can be rebuilt from published app metadata plus `live/frontend/` once a rebuild command exists.

Fresh installs require `CF_DNS_API_TOKEN` scoped to the Automaton zone and `ACME_EMAIL` for Traefik DNS-01 certificate issuance.

## Single Service Redeploy

API:

```bash
cd /opt/automaton
./scripts/prod-redeploy-api.sh
```

This uses the same API cutover behavior as `prod-redeploy-all.sh`: after health-checked cutover, the active API publishes durable reconnect commands for Runners still connected to the previous API color. The old API color is stopped after the configured overlap unless `--keep-previous` is used. No Runner drain or registration acknowledgement is required.

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
./scripts/prod-redeploy-website.sh
```

App host runtime/static infrastructure:

```bash
cd /opt/automaton
./scripts/prod-redeploy-app-host.sh
```

Use this only when the `apps.automaton.monster` Traefik runtime configuration or `/runtime/automaton-api.js` changes. Publishing a user app does not use this script.

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

## Rollback Flow

Rollback is fastest when the last deploy used `--keep-previous`, because the previous color is still running.

First, check the current live color:

```bash
cd /opt/automaton
./scripts/prod-switch-blue-green.sh status
docker compose -f docker-compose.prod.yml ps
```

If all traffic currently points to `green` and the previous healthy color is `blue`, roll all public services back to blue:

```bash
cd /opt/automaton
./scripts/prod-switch-blue-green.sh all blue
```

If all traffic currently points to `blue` and the previous healthy color is `green`, roll all public services back to green:

```bash
cd /opt/automaton
./scripts/prod-switch-blue-green.sh all green
```

Rollback can also be done one service at a time:

```bash
cd /opt/automaton
./scripts/prod-switch-blue-green.sh api blue
./scripts/prod-switch-blue-green.sh studio blue
./scripts/prod-switch-blue-green.sh website blue
```

After rollback, verify both origin and Cloudflare paths:

```bash
curl -k --resolve api.automaton.monster:443:127.0.0.1 https://api.automaton.monster/healthz
curl -k --resolve studio.automaton.monster:443:127.0.0.1 https://studio.automaton.monster -I
curl -k --resolve apps.automaton.monster:443:127.0.0.1 https://apps.automaton.monster/runtime/automaton-api.js -I
curl -k --resolve automaton.monster:443:127.0.0.1 https://automaton.monster -I
curl -I https://api.automaton.monster/healthz
curl -I https://studio.automaton.monster
curl -I https://apps.automaton.monster/runtime/automaton-api.js
curl -I https://automaton.monster
```

If the previous color was already stopped because the deploy ran without `--keep-previous`, do not use route switching as rollback. Redeploy a known-good commit or image to the inactive color, wait for health checks, then switch traffic normally.

The app host itself does not roll back with blue/green route switching because it is the stable Traefik runtime edge. Roll back platform app-host changes by reverting the runtime SDK or route configuration and rerunning the app-host refresh or full redeploy. Roll back an individual published app by unpublishing or republishing that app.

## One-Time Production Setup Commands

If the server is already prepared and you just need the minimal command sequence for one-time production setup:

```bash
cd /opt/automaton
cp .env.prod.example .env
# edit .env and /opt/automaton/config/appsettings.Production.json
docker compose -f docker-compose.prod.yml up -d traefik
docker compose -f docker-compose.prod.yml up -d --build api-blue studio-blue website-blue
./scripts/prod-reset-blue-green.sh
./scripts/prod-redeploy-all.sh --keep-previous
```

The explicit Traefik startup obtains the default app certificate through Cloudflare DNS-01. The persistent `prod-traefik-acme` volume must remain intact across redeploys.
The explicit blue startup plus `prod-reset-blue-green.sh` creates a predictable routing baseline before the first regular blue/green redeploy.

## Infrastructure Services

These services do not rebuild from local source in this repo:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d mysql rabbitmq traefik
```

Pull newer upstream images first if needed:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml pull mysql rabbitmq traefik
docker compose -f docker-compose.prod.yml up -d mysql rabbitmq traefik
```

Traefik mounts the persistent ACME volume and dynamic route directory. The API writes route configuration; do not remove either during normal deployment.

## TLS Certificates

The Traefik default certificate for public apps must cover both
`apps.automaton.monster` and `*.apps.automaton.monster`. The wildcard requires DNS-01;
do not assume a certificate for `*.automaton.monster` covers the nested app hostnames.
The production Traefik dynamic configuration uses `defaultGeneratedCert` with the
Cloudflare DNS-01 resolver, so generated app routes should retain `tls: {}` and must not
request a certificate per slug.

Issue or refresh the live certificate bundle:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d traefik
```

Traefik renews the certificate automatically. Do not run Certbot or add a separate renewal cron job; that would create a competing certificate authority for the same public domains.

## Runner And CLI Downloads

The production website deploy no longer rebuilds downloadable runner or CLI assets.

Current state:

- `./scripts/prod-redeploy-website.sh` deploys only the website image
- `./scripts/prod-redeploy-all.sh` deploys only the API, Studio, and website images
- standalone runner archives are built separately by [`azure-pipelines-runner.yml`](../../azure-pipelines-runner.yml)
- if public `/downloads/...` assets are needed, publish them from CI artifacts or a dedicated asset-publish job outside the website blue/green deploy path

Reason:

- cross-platform runner packaging is slow
- tying asset builds to every website deploy made redeploys slower and the repo no longer contains the old `website-cli-assets` compose service

Recommended direction:

- keep website deploys focused on app containers
- publish runner archives from CI artifacts to a stable downloads location on Hetzner or object storage
- if needed later, add a small dedicated `publish-runner-downloads` script or pipeline that syncs artifacts to `/downloads/runner/latest` without rebuilding the website

Current recommended host path:

- `/opt/automaton-downloads/runner/releases/<version>/`
- `/opt/automaton-downloads/runner/latest/`

Recommended public URL:

- `https://studio.automaton.monster/downloads/runner/latest/...`

## Logs

For disk preflight and cleanup, follow [production Docker maintenance](../../.agents/skills/production-docker-maintenance/SKILL.md), including storage attribution and the authorization boundaries for recovery history.

**Storage maintenance record — 2026-10-05:** authorized cleanup removed obsolete unused image revisions, closed MySQL binary-log history and historical backup files. Live tables/volumes and immediate blue-green rollback images were preserved. MySQL binary-log expiry was persisted at seven days (`604800` seconds); this is the current host setting, not a universal retention requirement. The historical local backup directories were emptied, so their previous restore points and the purged incremental history are unavailable. Free space reached 21.3 GB and production health verification passed. The API release was initially stopped during its build before cutover. It subsequently resumed as `a3633ddb`, and focused M2 response/reporting acceptance passed; see the [historical roadmap evidence](https://github.com/rzvdaniel/automaton/blob/57941d9971f58409356a74cbcf2529292c2f8a61/docs/roadmap/managed-platform-completion-plan.md#capacity-responsereporting-release-and-live-acceptance--2026-10-05).

Phase 1 production logging on the single Hetzner VPS is intentionally split:

- audit events go to MySQL in `AuditEvents`
- diagnostic application logs go to rolling files under `/app/logs`
- container stdout/stderr stays enabled with Docker `json-file` rotation
- retention is bounded in both app config and Docker logging

Current defaults:

- audit event retention: `90` days via `Logging:Audit:RetentionDays`
- API rolling files: `14` daily files via `Logging:File:RetainedFileCountLimit`
- Docker container logs: `10m x 5` per container in `docker-compose.prod.yml`

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

The app host is served by the top-level `nginx` container, so app-host access and cache/header issues appear in the nginx logs.

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
```

Run the full production smoke check:

```bash
cd /opt/automaton
./scripts/prod-verify.sh
```

## Disk Cleanup

### Retention and admission policy

The repository provides opt-in host configuration and bounded maintenance. Installing it and restarting host services are separate operator actions; a source merge does not apply these policies to existing hosts.

- **Images:** preserve every image referenced by a running or stopped container, the current deployment and one explicitly protected rollback per component. Other tagged images are candidates only in repositories named by `/etc/automaton/storage-images.allow`. No volume/container pruning. API/Studio/Website current/previous revisions are additionally protected from blue-green state. Worker rebuilds pin the exact prior container image as `automaton-prod-worker:protected-rollback`. Recovery artifacts with unknown Git provenance and independently installed Runners must be explicitly listed. Do not use `latest` as a rollback reference.
- **Build cache:** 2 GB GC target on the actual builder, using Docker's native GC (not a scheduled blanket prune). This is a target for reclaimable cache, not a hard limit during builds. For emergency one-time cleanup, use `docker builder prune -af --keep-storage 2GB` only when the default Docker builder owns that cache and no build is active.
- **Journals:** 512 MiB persistent, 128 MiB runtime, seven-day age retention; active files can temporarily exceed archived-history bounds. Install the supplied journald drop-in.
- **Container logs:** production Compose and standalone Runner Compose use `json-file`, 10 MiB × three files. Daemon defaults cover other newly created containers. Existing containers need controlled recreation; do not recreate workloads just to adopt logging limits during maintenance.
- **Application file logs:** API/Worker diagnostic sinks roll daily and at `Logging:File:FileSizeLimitBytes` (default 10 MiB), retaining `Logging:File:RetainedFileCountLimit` files (default 14). Runner's Serilog file sink uses 10 MiB and seven files. These are independent of Docker stdout logs and configurable through the existing logging settings. A single large event can exceed a file threshold. API audit data is separate and retains its existing policy; do not delete audit or workload data as log cleanup.
- **Temporary/migration artifacts:** retire each completed artifact when its recovery purpose ends, after checking mount/configuration references. Keep required recovery copies off the active host filesystem. Give future disposable tasks an owner and expiry date; use `/var/tmp/automaton-maintenance` only for disposable staging (tmpfiles expires inactive content after 14 days). Never put live secrets, mounted configuration or the sole recovery copy there. Existing build contexts already use EXIT cleanup; interrupted historical directories still require attribution before deletion.
- **MySQL:** production Compose explicitly defaults to `MYSQL_BINLOG_EXPIRE_LOGS_SECONDS=604800` (seven days). Confirm this against backups and all replicas before deployment; override it if recovery requires longer. Age retention is not a disk cap and does not replace backups. Verify `@@global.binlog_expire_logs_seconds` and auto-purge after rollout; existing persisted MySQL settings can override startup defaults. Changing a template does not alter a running server. For authorized existing-history cleanup, use `SHOW BINARY LOGS` and MySQL `PURGE BINARY LOGS TO '<verified-cutoff>'`; never unlink database files or reset GTID state. Inspect offline replica/backup dependencies before selecting a cutoff. Production disables general/slow-query file logging inherited from the development cnf; error diagnostics continue through Docker's rotated stderr logs. Enable SQL tracing only with a separate rotation policy. No existing SQL trace files are deleted by these scripts.

Capacity checks use the relevant mounted ext4/XFS/Btrfs/ZFS filesystems separately: root, Docker's configured root, the default containerd store, build context/temp storage and container mount sources (including database/workload disks). Small unrelated partitions such as `/boot` do not block deployment. Set newline-separated `AUTOMATON_STORAGE_PATHS` for additional paths, such as a non-default containerd root or a future bind mount not yet referenced by a container. They report **WARNING below 5 GiB or 15% free**, **CRITICAL below 2 GiB or 5%** and return nonzero. Timer output/failures go to the system journal; connect those failures to the operator's existing alerting. This does not install an external notification service.

Production scripts check before infrastructure startup/pulls and again before source builds. They refuse below **10 GiB or 15% free** without creating a build context or changing containers. Set `AUTOMATON_STORAGE_MIN_FREE_GIB` higher for the measured peak replacement/build requirement: running and rollback images stay on disk while a replacement is built/pulled. This simple admission floor is not a reservation or guarantee against concurrent unrelated disk growth. Before manual pulls/builds or independent Runner installation, run `bash scripts/host-storage.sh preflight` on that host too. Existing workload admission still guards app/Action work on workspace storage.

Routine application releases start missing infrastructure without recreating existing MySQL, RabbitMQ, Worker or Traefik containers. Application build/reuse/recovery starts use `--no-deps` after those dependencies are checked. Shared-infrastructure configuration changes and software upgrades require a separate deliberate operation; otherwise a Compose config difference can discard a broker's required operational hostname/image override.

If the required live data, current images and rollback set cannot coexist with that headroom, expand the disk rather than deleting live state or repeatedly reducing retention.

### Install the host policies

Review the files under [docker/host-maintenance](../../docker/host-maintenance) on each host. Merge the daemon example as described above; validate and schedule any Docker restart. Install journald and the capacity/maintenance timers from the repository:

```bash
sudo install -Dm644 docker/host-maintenance/journald.conf /etc/systemd/journald.conf.d/automaton-storage.conf
sudo systemctl restart systemd-journald
sudo install -Dm644 docker/host-maintenance/tmpfiles.conf /etc/tmpfiles.d/automaton-maintenance.conf
sudo systemd-tmpfiles --create /etc/tmpfiles.d/automaton-maintenance.conf
sudo install -Dm755 scripts/host-storage.sh /usr/local/sbin/automaton-host-storage
sudo install -m644 docker/host-maintenance/automaton-*.service docker/host-maintenance/automaton-*.timer /etc/systemd/system/
sudo install -d -m755 /etc/automaton
sudo systemctl daemon-reload
sudo systemctl enable --now automaton-storage.timer
```

The supplied tmpfiles policy also creates the shared lock with group `docker` after reboot. The deployment account must belong to that group. Deployment scripts and the image-maintenance timer share this lock. Manual maintenance must use it too.

Create a root-owned `/etc/automaton/storage-images.allow` containing verified local non-`latest` image references, one per line (comments begin with `#`). Include the current and chosen rollback reference for each managed component; for Worker include `automaton-prod-worker:protected-rollback` after verifying/creating that tag from the exact current healthy image before its first replacement. Do not guess Git revisions. Missing references cause image maintenance to fail closed **before deletion**. Blue-green state also requires its protected revisions to exist; if previous cleanup intentionally removed them, reconcile that operational state explicitly rather than inventing replacement provenance.

The image timer defaults to `/opt/automaton/.prod-runtime/nginx/state/blue-green-state.env`. Set `AUTOMATON_STORAGE_DEPLOYMENT_STATE` in `/etc/default/automaton-storage` if installed elsewhere; workload-only hosts can point it to a deliberately absent path. The allow-list alone then governs eligible repositories and protected artifacts. The service file accepts an alternative `AUTOMATON_STORAGE_IMAGE_ALLOWLIST` in the same environment file. Restrict edits to operators. Use the same variables when invoking maintenance from a different checkout.

```bash
sudo flock /run/lock/automaton-deployment.lock /usr/local/sbin/automaton-host-storage maintain
sudo systemctl enable --now automaton-image-maintenance.timer
systemctl list-timers 'automaton-*'
journalctl -u automaton-storage.service -u automaton-image-maintenance.service
```

Successful production deploys also run bounded image cleanup; missing/invalid protection configuration emits a warning and leaves images untouched. Cleanup failures do not roll back a healthy deployment. Timers never purge database logs or backups. MySQL expiry, Docker GC and journald each own their retention.

Maintenance evidence, 24 October 2026: host-level policies were installed on both existing servers. Capacity and image-maintenance services passed, both actual Docker builders reported the 2 GiB GC policy, and the daemon restarts preserved all running container IDs/start times using live-restore. Ubuntu's standard Buildx package was installed on the workload host without upgrading Docker. Seven-day MySQL binlog retention was verified; unbounded SQL trace logging was disabled without deleting existing logs or changing table data. The initially paused control-plane image-cleanup timer was enabled after the production release below reconciled its checkout and protected the new artifacts. Both hosts' timers are active. API/Worker and the fresh production Runner picked up the file-log limits during that release; retained older containers keep their existing bounded settings. No external alert delivery was installed: service failures are available through the system journal.

### Production own-use release: 24 October 2026

- API, Studio, Worker and workstation CLI use `7ce95744`; Azure Runner [build 5203](https://dev.azure.com/rzvdaniel/Automaton/_build/results?buildId=5203) built that exact revision. Installed Runner digest: `sha256:63145537283e3304c03666cdd76db5c91ab2e9695ce0ecccfb76400e13d19011`. Production selectors are API green and Studio blue; Website was unchanged.
- The previous checkout's hotfix commits were preserved on `production-before-main-20261024`; local main then fast-forwarded without resetting local configuration. Migration history contained no Node migrations. The sole pending migration, `20261020212303_AddMachineRunnerStorageTelemetry`, added five nullable Runner columns; it applied successfully without resetting the database or removing legacy records.
- A consistent SQL dump and API app/published-storage/configuration archives were retained off-host on the workload server in `/root/automaton-production-release-7ce95744-recovery`, accessible only to administrators. The SQL copy passed gzip and SHA-256 checks; a restore exercise was not performed. Legacy workloads/volumes and prior API/Studio/Worker images remain. No automatic legacy workload conversion was attempted.
- Compose dry-run exposed infrastructure recreation, so the live release staged only the app components with `--no-deps`, using the existing health, selector and durable tunnel-reconnect helpers. RabbitMQ/MySQL were not recreated. Five reconnect commands were queued without failure. Stop the previous API after the reconnect/overlap before starting new-version app operations; retain its stopped container/image for rollback. During this release the old API claimed the first app deployment, which completed under main after its lease expired; no duplicate request was submitted.
- The existing production administrator used the CLI to create, assign and deploy a fresh Hello World app through the new Machine Management Runner. [Public Hello World](https://hello-world.apps.automaton.monster/) returned trusted HTTPS/HTTP 200 and rendered in a browser. Redeploying version 1 to version 2 preserved the marker written only under `AUTOMATON_APP_DATA_DIR`. Both operations reached `Succeeded`; the Hosted Runner count remained nine. Container inspection confirmed 1 CPU, 1 GiB memory, 256 PIDs and a read-only root filesystem.
- `prod-verify.sh` passed; Studio's sign-in page rendered and the existing CLI administrator credentials remained valid. Public registration stayed disabled. This is personal-use web deployment evidence, not Action, two-user, cancellation or combined restart/replay acceptance. Backup automation and Runner self-update remain deferred.

### Investigate before emergency cleanup

```bash
df -hT
docker system df -v
docker info --format '{{.DockerRootDir}}'
sudo du -xhd1 /var/lib /var/log /opt
sudo journalctl --disk-usage
```

Docker can use `/var/lib/containerd` on root even with Docker's root on a separate disk. Inspect active containerd tasks before attributing old-looking stores; never manually delete their snapshots. Shared image/cache accounting overlaps. Never use blanket image/volume pruning. Attribute any remaining growth to its owner and recovery purpose before deleting it, then recheck free space and service health.

References: [Docker GC](https://docs.docker.com/build/cache/garbage-collection/), [Docker log rotation](https://docs.docker.com/engine/logging/drivers/json-file/), [journald limits](https://www.freedesktop.org/software/systemd/man/latest/journald.conf.html), [MySQL binary-log expiry](https://dev.mysql.com/doc/refman/8.0/en/replication-options-binary-log.html).

## Server Health Checks

For a quick server summary, run:

```bash
echo "=== DISK ===" && df -h / && \
echo "" && \
echo "=== MEMORY ===" && free -h && \
echo "" && \
echo "=== CPU ===" && lscpu | grep "^CPU(s):"
```

For real-time monitoring, use:

```bash
htop
```

If `htop` is not installed:

```bash
sudo apt install htop -y
```

`htop` is worth installing on production hosts. It is usually the fastest way to spot CPU pressure, memory pressure, or a noisy process while investigating runner, API, MySQL, or deploy issues.

## Notes

- This is safer than `docker compose up -d --build website` because Traefik keeps serving the currently live color until the new color passes health checks and the selector file is switched.
- Cloudflare should no longer need the DNS proxy off/on workaround in normal deploys, because the hostname never points at a half-restarted backend and full-stack cutover updates Traefik once.
- API blue/green assumes database changes are backward-compatible for the duration of the cutover. Avoid schema changes where the old API crashes immediately after the migration is applied.
- The blue/green scripts run `docker compose ... --remove-orphans`, so old pre-migration containers like `automaton-prod-website-1` / `-2` get cleaned up automatically on the next deploy.
- `apps.automaton.monster` is not blue/green. It is the stable Traefik runtime edge; platform rollback and individual app rollback are separate operations.

## External Runner broker TLS

Production Compose exposes Traefik's `runner-broker` TLS entry point on port 5671, bound to the existing `INFRASTRUCTURE_RABBITMQ_HOST` address. Hosted Runner broker configuration uses that address, enables TLS and sets the expected certificate name to `API_DOMAIN`. Traefik terminates TLS using its existing Cloudflare ACME resolver and forwards AMQP only inside the Docker network to `rabbitmq:5672`. The existing Infrastructure Runner plaintext listener remains private and unchanged.

Runners must reach the private broker address (for example through the existing private network/VPN). This does not publish a public BYO endpoint or configure DNS/firewalls. The certificate must match `API_DOMAIN` and chain to the Runner's OS trust; verify revocation access as well. No certificate-validation bypass is allowed. Traefik manages renewal; no copied private key or additional CA is required.

For initial rollout, preserve the existing Compose/runtime files and deployment state, render configuration with the supported production setup scripts, then recreate Traefik so the new static entry point and port binding take effect. This briefly interrupts edge traffic. Verify routing and an actual Runner-to-broker TLS handshake with hostname/chain validation before releasing the new API and Runner. Normal redeploy rendering retains this TCP router across API color changes. If TLS verification fails, restore the previous Traefik configuration/port bindings and retain the old API/Runner images; do not downgrade the new execution path to plaintext.
