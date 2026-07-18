---
name: deployment-prod
description: Use when changing Automaton production deployment scripts, docker-compose.prod.yml, nginx configuration, blue/green deployment, cert scripts, health checks, or production verification.
---

# Production Deployment

Use this skill when touching:

- `scripts/prod-*.sh`
- `docker-compose.prod.yml`
- nginx configuration
- certificate scripts
- blue/green state or upstream switching
- production health checks
- production verification docs

## Current production model

Automaton production uses Docker Compose and nginx blue/green upstream switching.

Services use blue/green variants such as:

- `api-blue` / `api-green`
- `studio-blue` / `studio-green`
- `website-blue` / `website-green`

Live color is tracked under:

```txt
.prod-runtime/nginx/state/blue-green-state.env
```

Upstream selectors live under:

```txt
.prod-runtime/nginx/upstreams/
```

## Rules

- Keep the previous color running until the new color is healthy.
- Warm up new containers before switching traffic.
- Make rollback obvious.
- Do not hide failed warmups with broad `|| true` unless the failure is intentionally non-blocking and documented.
- Do not reload proxy config before generated upstream files are valid.
- Do not make rollback harder.
- Prefer read-only diagnostics before production changes.
- Consider Cloudflare proxied mode when debugging external access.
- Managed app traffic is served directly by the runtime edge from runner endpoints; API SignalR draining protects control-plane operations, not app request availability.
- After API cutover, request reconnect-when-idle from the old color and poll its drain status before stopping it. Treat the configured grace period as a maximum timeout, not an unconditional sleep.
- Preserve rollback when post-cutover local origin verification fails.

## Before changing deployment logic

Identify:

- current live color
- target color
- affected services
- warmup endpoint
- health check command
- cutover step
- proxy reload step
- rollback path
- what happens if the new color fails health checks

## Verification

Run syntax checks for changed scripts:

```bash
bash -n scripts/prod-redeploy-all.sh
bash -n scripts/prod-redeploy-api.sh
bash -n scripts/prod-redeploy-studio.sh
bash -n scripts/prod-redeploy-website.sh
bash -n scripts/prod-verify.sh
bash -n scripts/prod-reset-blue-green.sh
```

Only run commands for files that exist.

For Docker Compose changes, reason through service names, networks, volumes, health checks, and dependencies.

Read:

```txt
.agent/harness/verification.md
```

## Production connection and redeploy workflow

When the user asks to update the live Automaton deployment, connect through the configured SSH alias:

    ssh automaton
    cd /opt/automaton

The automaton account has passwordless sudo. Use sudo -n so an unattended agent never hangs waiting for a password. Production runtime directories and parts of the checkout may be root-owned.

Start with read-only diagnostics:

    ssh automaton "cd /opt/automaton && git status -sb && git log -1 --oneline --decorate && sudo -n bash scripts/prod-switch-blue-green.sh status && docker compose -f docker-compose.prod.yml ps"

Before pulling:

- inspect tracked modifications with git diff --stat
- preserve unrelated production changes
- compare modified files with origin/main before deciding whether they are already upstream
- never reset or discard a tracked production change silently
- never stage production secrets, private keys, .env, secrets/, virtual environments, backups, or runtime state

Pull only with a fast-forward:

    ssh automaton "cd /opt/automaton && sudo -n git pull --ff-only origin main"

Selectively rebuild services based on the merged diff:

    # API/backend, Infrastructure, migration, or messaging changes
    ssh automaton "cd /opt/automaton && sudo -n ./scripts/prod-redeploy-api.sh"

    # Studio frontend changes
    ssh automaton "cd /opt/automaton && sudo -n ./scripts/prod-redeploy-studio.sh"

    # Worker or messaging changes
    ssh automaton "cd /opt/automaton && sudo -n bash scripts/prod-redeploy-worker.sh"

    # Public website changes
    ssh automaton "cd /opt/automaton && sudo -n ./scripts/prod-redeploy-website.sh"

The API, Studio, and Website scripts use the inactive blue/green color, wait for health, verify network reachability and public warmup, switch nginx/Traefik routing, and remove the previous color after the overlap/reconnect period. The Worker script rebuilds and health-checks the single Worker container. Do not use the full-stack script when only a subset changed.

For migrations, inspect affected production rows before deployment. If a migration is explicitly disposable and the user authorizes deletion of test data, delete only identified test rows before the API starts. Do not present a destructive development migration as production-safe.

Finish with:

    ssh automaton "cd /opt/automaton && sudo -n bash scripts/prod-verify.sh"

The handoff should state the pulled commit, any local production change that was preserved or reverted, each redeployed service, successful health/cutover checks, services skipped, migration/data-safety status, and the final live colors:

    Pulled main to commit <sha>
    API redeployed to <color> and passed health/warmup checks
    Studio redeployed to <color> and passed health/warmup checks
    Worker rebuilt and restarted successfully
    Website was unchanged and not redeployed
    Live colors:
    API: <color>
    Studio: <color>
    Website: <color>
