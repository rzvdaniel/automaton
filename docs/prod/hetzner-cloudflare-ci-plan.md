# Hetzner + Cloudflare + CI Deployment Plan

This plan turns the exploratory notes in `docs/prod/hetzner-cloudflare-ci.md` into an implementation order. It assumes the first production-like deployment is intentionally small: one Hetzner server, Docker Compose, Traefik, Cloudflare DNS/CDN, Azure Pipelines building images, and a path to move runner traffic off the main server later.

## Target Architecture

Start with one Hetzner server as the first control plane and runner node:

- Cloudflare owns DNS, TLS edge, caching, and later Cloudflare Tunnel routing.
- Traefik is the reverse proxy on the Hetzner server.
- Docker Compose runs the platform services: API, Studio, marketing site, MySQL, static app host, and one or more local Automaton Runner containers.
- Azure Pipelines builds Docker images and pushes them to a registry.
- The server pulls images and restarts services from Compose.
- User apps use reserved `app*` domains for frontend hosting.
- The Automaton API is both the control plane and the runtime gateway for app-to-runner calls in the first deployment. The runner reaches the API over the existing SignalR hub/tunnel channel.

## Decisions To Lock First

1. Choose the server size.
   - Recommended first server: Hetzner CPX31 if budget allows.
   - Acceptable lean start: CX22.

2. Use MySQL for the first deployment.
   - The exploratory document mentions PostgreSQL early, but the current project guidance and backend setup expect MySQL/MariaDB.

3. Align runtime version with the actual codebase.
   - Current project notes say ASP.NET Core 9.0.
   - Do not build deployment images around .NET 10 unless the repo is upgraded first.

4. Lock the Automaton domain model before implementation.
   - Production domain: `automaton.monster`.
   - Automaton server API: `api.automaton.monster`.
   - Studio: `studio.automaton.monster`.
   - Website: `automaton.monster` and `www.automaton.monster`.
   - User app frontends: `app-<slug>.automaton.monster`.
- User app API calls: `api.automaton.monster`, routed by Automaton to the correct runner through the existing SignalR tunnel.
   - Standard DNS wildcards work on whole labels, so Cloudflare DNS should use `*.automaton.monster`; Automaton enforces the `app-` prefix in validation.
   - If you prefer no hyphen, use `app<slug>.automaton.monster`, but keep the prefix mandatory.

5. Reserve platform domains before user slugs go live.
   - Never allow users to claim bare top-level subdomains directly; user domains must start with the approved app prefix.
   - Reserve exact domains:
     - `automaton.monster`
     - `www.automaton.monster`
     - `api.automaton.monster`
     - `studio.automaton.monster`
     - `admin.automaton.monster`
     - `auth.automaton.monster`
     - `status.automaton.monster`
     - `docs.automaton.monster`
     - `support.automaton.monster`
     - `billing.automaton.monster`
     - `cdn.automaton.monster`
     - `assets.automaton.monster`
     - `mail.automaton.monster`
     - `system.automaton.monster`
     - `internal.automaton.monster`
     - `runner.automaton.monster`
     - `proxy.automaton.monster`
     - `edge.automaton.monster`
     - `gateway.automaton.monster`
     - `app.automaton.monster`
     - `apps.automaton.monster`
   - Reserve exact app-prefixed names that could be confused with platform areas:
     - `app-api.automaton.monster`
     - `app-admin.automaton.monster`
     - `app-auth.automaton.monster`
     - `app-billing.automaton.monster`
     - `app-cdn.automaton.monster`
     - `app-docs.automaton.monster`
     - `app-internal.automaton.monster`
     - `app-mail.automaton.monster`
     - `app-runner.automaton.monster`
     - `app-status.automaton.monster`
     - `app-studio.automaton.monster`
     - `app-support.automaton.monster`
     - `app-system.automaton.monster`

6. Decide Cloudflare TLS mode correctly.
   - Use Cloudflare "Full (strict)" with a Cloudflare Origin Certificate or Let's Encrypt certificate on Traefik.
   - Do not assume Cloudflare edge HTTPS alone is enough if origin traffic is also meant to be HTTPS.

## Implementation Order

### 1. Prepare Docker Images

Create or verify Docker images for:

- `automaton-api`: ASP.NET Core API.
- `automaton-studio`: built Vue Studio served by nginx or another static server.
- `automaton-web`: marketing/presentation website.
- `automaton-app-host`: static user app host that resolves app content by hostname.
- `automaton-runner`: the existing Automaton Runner image, including the .NET runner, SignalR connection to the API, and local FastAPI process.
- Later only: `automaton-node-agent`, if Automaton needs server-side orchestration to start/stop many per-app containers automatically.

Important runner choice:

- Do not rebuild a Docker image for every user app.
- Use the existing runner image and persist uploaded app backend code in the runner data volume.

### 2. Externalize Configuration

Move production configuration out of images:

- Use environment variables for secrets and small values.
- Use mounted config files for larger structured config if needed.
- Store persistent files under `/opt/automaton`.

Minimum server paths:

- `/opt/automaton/compose`
- `/opt/automaton/config`
- `/opt/automaton/mysql`
- `/opt/automaton/apps`
- `/opt/automaton/runners`
- `/opt/automaton/backups`

Required API config:

- Database connection string.
- JWT/signing keys.
- Public base URLs: Studio, API, app root, app API root.
- Docker registry image names and tags.
- Cloudflare account/zone settings, added later when tunnel automation starts.

### 3. Add Domain and Slug Support In Automaton

Backend work:

- Add fields for frontend domain, app slug, deployment status, current version, and selected execution runner id if they do not already exist.
- Implement slug generation with collision handling: `my-app`, `my-app-2`, `my-app-3`.
- Enforce reserved names in API validation and database constraints.
- Store `apiBaseUrl` per app as `https://api.automaton.monster` unless a future custom routing mode overrides it.

Frontend/app publishing work:

- Ensure generated/static apps receive runtime config via a small generated file such as `/automaton-config.json` or an injected `window.__AUTOMATON_CONFIG__`.
- Generated apps should call `api.automaton.monster` through the existing app runtime endpoints:
  - published controllers: `/api/web-apps/{slug}/controllers/{controller}/actions/{action}/execute`
  - preview controllers: `/api/web-apps/preview/{appId}/controllers/{controller}/actions/{action}/execute`
  - published flows: `/api/web-apps/{slug}/flows/{flow}/execute`
  - preview flows: `/api/web-apps/preview/{appId}/flows/{flow}/execute`

### 4. Keep Runner Selection Explicit Before Adding Node Orchestration

The current implementation already has the important MVP abstraction: each app stores `ExecutionRunnerId`, and the API selects that runner when executing app backends or flows.

Keep this as the first production model:

- `WebApps.ExecutionRunnerId` selects the runner.
- `Runners.ConnectionId` identifies the live SignalR connection.
- Backend deploy uses `UploadControllerScript`.
- Backend execution uses `TunnelRequest` -> runner -> local FastAPI.
- File upload/download uses SignalR chunk messages.

Add node orchestration later, when Automaton needs to start/stop many runner containers itself:

- `Nodes`: id, name, type, status, capacity, used memory, used CPU, last heartbeat.
- `Apps` or deployment table: node id, container id/name, frontend domain, execution runner id, status.
- Prefer a node agent that owns Docker access.
- Avoid mounting the Docker socket directly into the public API container unless this is accepted as a temporary MVP risk.

### 5. Bootstrap Cloudflare and Domain

Cloudflare setup:

- Buy or transfer the domain.
- Set Cloudflare nameservers at the registrar.
- Add DNS records:
  - `A automaton.monster -> Hetzner IP`, proxied.
  - `A *.automaton.monster -> Hetzner IP`, proxied.
- Set SSL/TLS mode to Full (strict).
- Generate an origin certificate or configure Traefik with Let's Encrypt DNS challenge.
- Add cache rules for static app assets, but bypass cache for API routes.

### 6. Provision The Hetzner Server

Server setup:

- Create the VPS.
- Add a non-root deploy user.
- Disable root SSH login.
- Configure firewall:
  - Allow SSH from trusted IPs where practical.
  - Allow 80 and 443.
  - Deny direct database and app container ports.
- Install Docker and Docker Compose plugin.
- Create `/opt/automaton` directories.
- Log in to the Docker registry.

### 7. Create Production Compose Stack

Compose services:

- Traefik
- MySQL
- Automaton API
- Studio static service
- Website static service
- App host service
- Local Automaton Runner container(s)

Traefik routes:

- `automaton.monster` and `www.automaton.monster` -> website.
- `studio.automaton.monster` -> Studio.
- `api.automaton.monster` -> Automaton API.
- `app-<slug>.automaton.monster` -> app host.
- Runner containers are not exposed as public per-app domains. They connect outbound to the API SignalR hub, and `api.automaton.monster` reaches them through that hub.

Operational requirements:

- Use Docker networks so only Traefik exposes public HTTP(S) ports.
- Use named volumes or mounted directories for MySQL.
- Add health checks for API, Studio, app host, and runners.
- Secure or disable the Traefik dashboard.

### 8. Wire Azure Pipelines To Registry and Server

CI build:

- Build all platform images.
- Push immutable tags, preferably commit SHA or semantic version.
- Optionally also push `latest`, but never rely only on `latest`.

Deployment:

- First version can use SSH from Azure Pipelines:
  - `docker compose pull`
  - `docker compose up -d`
  - run migrations explicitly or via a controlled API startup process
- Store server `.env` and mounted config outside the repo.
- Add rollback by deploying the previous immutable image tag.

Downtime:

- Accept brief downtime for the first MVP if needed.
- Then add health checks and blue/green or rolling replacement for the API and Studio.

### 9. Implement Static App Publishing

Publishing flow:

1. User/CLI uploads static frontend artifact to Automaton API.
2. API validates ownership, slug, reserved names, and artifact shape.
3. API stores a versioned artifact under `/opt/automaton/apps/<app>/<version>`.
4. API writes app runtime config containing `apiBaseUrl`.
5. API atomically marks that version as current.
6. App host serves the current version based on the request hostname.

Important:

- Keep old versions for rollback.
- Do not let user app files overwrite platform routes.
- Add cache-busting for changed assets.

### 10. Validate Local Runner Backend Deployment

Phase 1 runtime path:

- Browser -> Cloudflare -> Traefik -> Automaton API -> SignalR hub -> local runner container -> FastAPI on `localhost:8001`.

Deployment flow:

1. Studio selects `ExecutionRunnerId` for the app.
2. Studio calls `POST /api/apps/{appId}/backend/deploy`.
3. API sends each backend script to the selected runner with `UploadControllerScript`.
4. Runner writes scripts to its persistent app data directory.
5. Published or preview app calls `api.automaton.monster`.
6. API resolves the app and selected runner.
7. API sends `TunnelRequest` over SignalR.
8. Runner forwards the request to local FastAPI at `http://localhost:8001/api/controller/{controller}`.
9. FastAPI loads the Python controller from disk and returns the action result.
10. Runner sends `TunnelResponse` back to the API, and the API returns the result to the browser.

Minimum safeguards:

- Per-runner-container resource limits.
- No privileged runner containers.
- No host network mode.
- Clean shutdown and restart path.
- Log retention policy.

### 11. Add Observability and Backups Before Real Users

Required before inviting users:

- MySQL backups to `/opt/automaton/backups` and off-server storage.
- App artifact backups.
- Basic server metrics: disk, memory, CPU.
- API logs and runner logs.
- Health/status page for API, Studio, app host, runner SignalR connection status, and runner FastAPI health.
- Alert when disk or memory crosses a threshold.

### 12. Add Cloudflare Tunnel For Remote/User-Prem Runners

Do this after local runner deployment works.

Keep Automaton's deployment/control tunnel separate from runtime traffic:

- Control plane: Studio/API -> Automaton node/runner management channel.
- Runtime gateway: Browser -> Cloudflare -> `api.automaton.monster` -> runner.
- Remote runner hop: Automaton API -> existing SignalR tunnel -> runner FastAPI.

Implementation:

1. Keep the existing SignalR tunnel as the default remote runner path.
2. Add Cloudflare Tunnel only if SignalR tunnel throughput, latency, or large-file behavior becomes a problem.
3. If Cloudflare Tunnel is added, store the tunnel route as an alternate runner target for Automaton API dispatch.
4. Store tunnel id, credentials reference, status, and last heartbeat.
5. Show tunnel status in Studio.
6. Retry reconnects and mark apps degraded when tunnels are offline.

Security:

- Treat Cloudflare tunnel tokens as secrets.
- Scope tokens as tightly as Cloudflare allows.
- Rotate tokens when runners are removed.

### 13. Move Runners To Additional Hetzner Servers

Once the first server hits sustained resource limits:

1. Provision a second Hetzner VPS.
2. Install Docker and Automaton Runner.
3. Register the node with Automaton.
4. New app deployments go to the least-loaded healthy node.
5. Move existing apps by stopping on old node, starting on new node, and updating the app deployment record.

Routing options:

- Short-term: main Traefik forwards to remote node IPs.
- Better: Automaton API dispatches to each registered runner over the existing SignalR channel.
- Add a node agent only when the API must provision and lifecycle-manage runner containers automatically.
- Add Cloudflare Tunnel only when the existing SignalR tunnel is no longer sufficient for runner traffic.

## First Working Milestones

1. Platform online:
   - `automaton.monster`, `studio.automaton.monster`, and `api.automaton.monster` work through Cloudflare.

2. CI/CD online:
   - Azure Pipelines builds images, pushes tags, and deploys to Hetzner.

3. Static app online:
   - A test app publishes to `app-<slug>.automaton.monster`.

4. Local backend online:
   - A test app calls `api.automaton.monster`, which routes the request to the correct local runner container.

5. Rollback works:
   - API image rollback and app frontend version rollback are both tested.

6. Remote runner proof:
   - A runner on another machine can receive routed calls from `api.automaton.monster` through the existing SignalR tunnel.

## Later Enhancements

- Custom domains for user apps.
- Sleep idle runner containers.
- Automated database backup restore test.
- Blue/green deployment script.
- Per-app rate limiting.
- Per-app usage metrics and billing hooks.
- Separate managed database if single-server operations become painful.
- Object storage for app artifacts when local disk becomes a limitation.
