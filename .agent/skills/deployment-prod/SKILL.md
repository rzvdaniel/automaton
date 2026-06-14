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
