---
name: deployment-prod
description: Use when deploying Automaton to production or changing its production deployment scripts, Docker Compose stack, Traefik/nginx routing, certificates, blue-green cutover, health checks, rollback, or verification.
---

# Production Deployment

Use this skill for both deployment operations and changes to the production deployment machinery. A live deployment requires an explicit user request; editing scripts, Compose, routing, certificates, health checks, or deployment documentation does not authorize a live rollout by itself.

## Current production model

Automaton production uses Docker Compose with nginx upstream selection and Traefik in the request path. API, Studio, and Website use blue-green variants:

- `api-blue` / `api-green`
- `studio-blue` / `studio-green`
- `website-blue` / `website-green`

The Worker is a single in-place service, not blue-green. Live colors are tracked under:

```text
.prod-runtime/nginx/state/blue-green-state.env
.prod-runtime/nginx/upstreams/
```

Hosted app frontend/backend requests are served directly from the runtime edge to Runner endpoints. API SignalR draining protects control-plane work and Runner state; it is not required for hosted app availability.

## Deployment-engineering rules

Before changing deployment logic, identify:

- current live color and target inactive color;
- affected services;
- build context, image, and Compose service names;
- warmup and health endpoints;
- Traefik/network reachability checks;
- nginx upstream generation and reload/cutover steps;
- rollback path if build, health, warmup, or origin verification fails;
- behavior when Cloudflare proxying is enabled.

Preserve these guarantees:

- keep the previous API, Studio, or Website color running until the replacement is healthy and warmed up;
- do not remove the previous color before cutover verification succeeds;
- do not reload nginx before generated upstream files are valid;
- do not hide failed warmups with broad `|| true` unless the failure is intentionally non-blocking and documented;
- after API cutover, request reconnect-when-idle from the old color and poll its drain status before stopping it; treat the configured grace period as a maximum timeout, not an unconditional sleep;
- preserve a rollback path when post-cutover local-origin verification fails;
- keep Runner reconnects jittered and prevent stale heartbeats/disconnects from overwriting a newer session;
- internal deployment/drain endpoints must accept loopback requests only or require dedicated internal authentication; never trust all RFC1918 addresses behind a reverse proxy;
- consider Cloudflare’s proxy/cache behavior when diagnosing public access.

For Compose changes, reason through service names, networks, volumes, health checks, dependencies, image tags, and restart behavior. A local solution build does not validate Docker build contexts or `COPY` coverage.

Run syntax checks for changed shell scripts, limiting commands to files that exist:

```bash
bash -n scripts/prod-redeploy-all.sh
bash -n scripts/prod-redeploy-api.sh
bash -n scripts/prod-redeploy-studio.sh
bash -n scripts/prod-redeploy-website.sh
bash -n scripts/prod-verify.sh
bash -n scripts/prod-reset-blue-green.sh
```

Read `.agent/harness/verification.md` for the repository verification matrix.

## Live deployment workflow

The configured production host is available through SSH alias `automaton`:

- checkout: `/opt/automaton`
- user: `automaton`
- passwordless sudo: use `sudo -n`

### 1. Preflight

Run read-only diagnostics before changing production:

```powershell
ssh -o BatchMode=yes automaton "cd /opt/automaton && git status -sb && git log -1 --oneline --decorate && sudo -n bash scripts/prod-switch-blue-green.sh status && docker compose -f docker-compose.prod.yml ps"
```

If Windows OpenSSH cannot authenticate, do not repeat deployment commands. Load the configured key into the Windows agent and test the alias:

```powershell
Start-Service ssh-agent
ssh-add C:\Users\Razvan\.ssh\automaton
ssh-add -l
ssh automaton "hostname"
```

To persist the agent startup setting, use elevated PowerShell:

```powershell
Set-Service ssh-agent -StartupType Automatic
Start-Service ssh-agent
```

Record production HEAD, current live colors, MySQL/RabbitMQ/Worker/Traefik health, and tracked/untracked checkout state.

### 2. Inspect, compare, and pull

Inspect tracked production modifications and compare the deployment revision before pulling:

```powershell
ssh automaton "cd /opt/automaton && sudo -n git fetch origin && git log --oneline --decorate -5 origin/main && git diff --stat HEAD..origin/main && git diff --name-status HEAD..origin/main"
```

Never reset, clean, stash, overwrite, or delete production state without explicit approval. Treat private keys, `.env`, `secrets/`, `venv/`, `backups/`, database files, generated runtime files, and test data as protected state. If tracked changes are not already represented by `origin/main`, stop and report them. Untracked production-only files may remain during a pull, but report that they were preserved.

Pull the normal production revision only with fast-forward:

```powershell
ssh automaton "cd /opt/automaton && sudo -n git pull --ff-only origin main"
```

If the pull is not fast-forwardable, stop. Do not create a merge commit or discard local state automatically.

### 3. Select affected services

Use the merged diff rather than redeploying the whole stack by default:

| Change area | Action |
|---|---|
| `src/Automaton.Api/**`, `src/Automaton.Infrastructure/**`, shared backend, migrations, or messaging | Redeploy API; rebuild Worker too when the shared change affects Worker runtime behavior |
| `src/automaton-studio-vue/**` | Redeploy Studio |
| `src/Automaton.Worker/**` or Worker runtime/configuration | Rebuild Worker in place |
| `src/automaton-vue/**` | Redeploy Website |
| Runner Dockerfile/image or runner provisioning changes | Follow the repository’s current Runner image/provisioning procedure; do not invent a Compose service or claim an existing Runner container was rebuilt |

Published CLI NuGet packages and Runner Docker Hub images are external artifacts. Verify that the deployed code references the intended version/tag when relevant; their publication alone does not imply rebuilding unrelated services.

### 4. Redeploy sequentially

Run selected commands from `/opt/automaton`, one service at a time:

```powershell
ssh automaton "cd /opt/automaton && sudo -n ./scripts/prod-redeploy-api.sh"
ssh automaton "cd /opt/automaton && sudo -n ./scripts/prod-redeploy-studio.sh"
ssh automaton "cd /opt/automaton && sudo -n bash scripts/prod-redeploy-worker.sh"
ssh automaton "cd /opt/automaton && sudo -n ./scripts/prod-redeploy-website.sh"
```

API, Studio, and Website scripts deploy to the inactive color, build the image, wait for health, verify Traefik reachability and public warmup, cut over routing, and retire the old color only after its overlap/drain period. The Worker script rebuilds and health-checks the single Worker container in place; do not describe it as blue-green. If any step fails, stop and preserve the current live service for diagnosis or rollback.

For API migrations, inspect API startup output or an explicit migration/status check and record the result. Inspect affected production rows before any destructive migration. Delete only explicitly disposable test rows when the user authorizes it; never generalize that into production-safe data conversion.

### 5. Verify and hand off

Run final verification:

```powershell
ssh automaton "cd /opt/automaton && sudo -n bash scripts/prod-verify.sh"
ssh automaton "cd /opt/automaton && git status -sb && git log -1 --oneline --decorate && docker compose -f docker-compose.prod.yml ps"
```

The handoff must state:

- pulled commit;
- tracked production changes preserved, reverted with approval, or blocking deployment;
- protected untracked production state preserved;
- redeployed services and successful build, health, warmup, cutover, drain, and rollback-safety checks;
- services intentionally skipped and why;
- migration status and any data-safety precondition;
- final API, Studio, and Website live colors;
- `prod-verify.sh` result and relevant warnings.
