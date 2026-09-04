---
name: production-deployment
description: Use when changing or operating Automaton production deployment scripts, Compose, Traefik routes, certificates, health checks, blue-green cutovers, or rollback.
license: Apache-2.0
---

# Production deployment

Use this skill for production operations and changes to production deployment machinery. A live deployment requires an explicit user request.

## Current model

API, Studio, and Website use blue-green services. The Worker is updated in place. Traefik serves public traffic and reads generated dynamic configuration. Hosted app traffic goes directly from the runtime edge to Runner endpoints.

Read the current scripts and [the production cheat sheet](../../../docs/prod/production-docker-cheat-sheet.md) before changing the workflow:

- `scripts/prod-redeploy-api.sh`
- `scripts/prod-redeploy-studio.sh`
- `scripts/prod-redeploy-website.sh`
- `scripts/prod-redeploy-worker.sh`
- `scripts/prod-verify.sh`

## Safety rules

1. Run read-only preflight checks on the production checkout, live colors, services, and protected state.
2. Keep Git network operations under the production `automaton` user. Keep network Git operations separate from privileged filesystem operations. Never copy credentials to root.
3. Inspect tracked changes before advancing production. Never reset, clean, stash, overwrite, or delete production state without explicit approval. Treat private keys, `.env`, `secrets/`, `venv/`, backups, databases, generated runtime files, and test data as protected.
4. Fetch `origin/main` and advance the checkout with a fast-forward only.
5. Select services from the merged diff. Do not rebuild unchanged services.
6. Render Traefik dynamic configuration atomically. Observe and validate the file-provider update before cutover. A Traefik ping alone does not validate the update.
7. Build and warm the inactive color before cutover. Keep the previous color until health, warmup, routing, and rollback checks pass.
8. For API cutovers, request Runner reconnect when idle, poll drain status, and verify candidate Runner IDs register on the new API color. If migration fails, preserve the old color. Roll API, Studio, and Website selectors back together for a full-stack rollback.
9. Run `scripts/prod-verify.sh` and report the deployed revision, services, live colors, migration status, and skipped services.

Do not hardcode a service color, public domain, port, or secret. Do not delete production data in a deployment change. Inspect any migration and affected rows first.

For shell changes, run `bash -n` on every changed production script. For production image or Dockerfile changes, validate the actual Docker build context in addition to a local solution build.
