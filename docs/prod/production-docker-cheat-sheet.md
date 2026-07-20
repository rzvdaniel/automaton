# Production Docker Cheat Sheet

This is the source of truth for the current nginx-based production stack.

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

- this applies to newly created containers
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

Optional:

```env
RUNNER_RECONNECT_GRACE_SECONDS=60
```

Use `RUNNER_RECONNECT_GRACE_SECONDS` as the maximum API runner-drain timeout after cutover. The old API color asks connected runners to reconnect when idle, and the deploy script stops waiting as soon as the old color reports no connected runners. This means runners have acknowledged idleness by disconnecting from the old API; the API does not directly count runner operations. The timeout is a control-plane protection for in-flight SignalR work; it is not required for hosted app availability because Traefik routes hosted app traffic directly to runner runtime endpoints.

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

`website` and `studio` are static frontend containers behind Traefik. API blue/green state is still tracked separately so runner control-plane draining remains explicit.

`apps.automaton.monster` is different. It is the stable runtime edge, backed by the API for platform runtime assets and by Traefik dynamic routes for managed applications. It does not have `apps-blue` or `apps-green` containers.

For API deploys, there is an extra runner-drain step:

- after the production edge switches `api.automaton.monster` to the new API color
- the previous API color tells its connected runners to reconnect when idle
- the deploy script polls the old color's internal drain status until all runners have reconnected, up to `RUNNER_RECONNECT_GRACE_SECONDS`
- then it stops the previous API color unless `--keep-previous` was used

This protects control-plane work such as workflow dispatch, file synchronization, log streaming, command execution, and runner lifecycle updates. Frontend files and backend requests for hosted apps continue through Traefik and do not depend on the old API SignalR connection.

Backward-compatibility note:

- if the previous API color is still running an older version that does not support the internal reconnect/drain endpoints yet, the deploy scripts log a warning and fall back to the maximum grace period
- in that case, hosted runners may remain attached to the old API color until that color is stopped or the runner container is restarted

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
./scripts/prod-redeploy-all.sh
```

Keep the previous color running for rollback:

```bash
cd /opt/automaton
./scripts/prod-redeploy-all.sh --keep-previous
```

Use `--keep-previous` for normal production deploys while the deployment is still fresh. It leaves the old live color running after Traefik switches to the new color, so rollback is only a route switch.

Important for runners:

- if you use `--keep-previous`, old API-color runners can continue finishing work on that old API color
- they will not be forced off immediately, so rollback stays fast
- when you do not use `--keep-previous`, the script waits for observable drain or `RUNNER_RECONNECT_GRACE_SECONDS`, then stops the previous API color, which forces any remaining runners on that color to reconnect through `api.automaton.monster`

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
git pull
chmod +x ./scripts/prod-*.sh
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
git pull
chmod +x ./scripts/prod-*.sh
./scripts/prod-redeploy-website.sh --keep-previous
./scripts/prod-verify.sh
```

For API-only or Studio-only deploys, use `prod-redeploy-api.sh --keep-previous` or `prod-redeploy-studio.sh --keep-previous` with the same verify step.

`prod-redeploy-all.sh` also refreshes the Traefik bootstrap config and `/runtime/automaton-api.js`. Single-service deploys do not need to touch the app runtime edge unless that SDK or route bootstrap config changed.

If only the app-host runtime SDK or Traefik runtime-router bootstrap config changed, use the dedicated app-host infrastructure script:

```bash
cd /opt/automaton
git pull
chmod +x ./scripts/prod-*.sh
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
6. Create `/opt/automaton/secrets/hosted_runner_ssh_key`, even as an empty placeholder if hosted runners are not in use.
7. Open firewall ports `22`, `80`, and `443`.
8. Run `chmod +x ./scripts/prod-*.sh`.
9. Configure `CF_DNS_API_TOKEN` and `ACME_EMAIL` in `.env`.
10. Start Traefik with `docker compose -f docker-compose.prod.yml up -d traefik`; it creates or renews the wildcard certificate in the persistent ACME volume.
11. Run `docker compose -f docker-compose.prod.yml up -d --build api-blue studio-blue website-blue`.
12. Run `./scripts/prod-reset-blue-green.sh`.
13. Run `./scripts/prod-redeploy-all.sh --keep-previous`.
14. Run `./scripts/prod-verify.sh`.

If migrating an existing production instance, copy `prod-api-appdata` and the database backup as the source of truth. `prod-published-apps` is generated output and can be rebuilt from published app metadata plus `live/frontend/` once a rebuild command exists.

Fresh installs require `CF_DNS_API_TOKEN` scoped to the Automaton zone and `ACME_EMAIL` for Traefik DNS-01 certificate issuance.

## Single Service Redeploy

API:

```bash
cd /opt/automaton
./scripts/prod-redeploy-api.sh
```

This uses the same runner reconnect flow as `prod-redeploy-all.sh`: after cutover, the previous API color signals connected runners to reconnect when idle, polls for observable drain up to `RUNNER_RECONNECT_GRACE_SECONDS`, then stops the old API color unless `--keep-previous` is used.

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

Use this only when `apps.automaton.monster` nginx config or `/runtime/automaton-api.js` changes. Publishing a user app does not use this script.

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
chmod +x ./scripts/prod-*.sh
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
- standalone runner archives are built separately by [`azure-pipelines-runner.yml`](/C:/Work/automaton/azure-pipelines-runner.yml)
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

See [`docs/runners/runner-download-publishing.md`](/C:/Work/automaton/docs/runners/runner-download-publishing.md) for the current native runner distribution direction and optional archive-hosting notes.

## Logs

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

When a Hetzner host starts running low on disk, check Docker usage first:

```bash
docker system df
df -h
```

If build cache is the main culprit, clean that first:

```bash
docker builder prune -a
```

Confirm with `y` when prompted.

After cleanup, check usage again:

```bash
docker system df
df -h
```

If Docker accounting and filesystem usage do not match, check Docker's actual root directory:

```bash
docker info | grep "Docker Root Dir"
```

Also inspect `/opt`, because local repo state, build context, or deployment artifacts may be large there:

```bash
sudo du -h --max-depth=1 /opt | sort -hr
```

Safe maintenance rules:

- prefer `docker builder prune -a` first because it reclaims old build cache without touching named volumes
- do not prune volumes as part of routine cleanup because MySQL data is likely stored in a Docker volume
- `du /var/lib/docker` can look smaller than `docker system df` when Docker uses a different root dir or when accounting includes reclaimable build cache layers

After a successful production deploy, it is reasonable to run:

```bash
docker builder prune -a -f
```

Do not run that in the middle of a deploy. Use it after the deployment and verification steps succeed.

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
