# Production Docker Cheat Sheet

Use these commands from the production server after pulling the latest code into `/opt/automaton`.

## Full Stack

Build and redeploy everything:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d --build
```

Stop everything:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml down
```

Show running services:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml ps
```

## Single Service Redeploy

API:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d --build api
```

Studio:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d --build studio
```

Website:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d --build website
```

## Infrastructure Services

These services do not build from local source in this repo. Redeploy them with `up -d`.

Traefik:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d traefik
```

MySQL:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d mysql
```

Socket proxy:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d socket-proxy
```

If you want newer upstream image versions for those services, pull first and then recreate:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml pull traefik mysql socket-proxy
docker compose -f docker-compose.prod.yml up -d traefik mysql socket-proxy
```

## Website CLI Downloads

Rebuild the downloadable CLI bundles only when `src/Automaton.Cli` changed:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml --profile build-assets build website-cli-assets
```

Then rebuild the website so it bakes those files into `public/cli`:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d --build website
```

## Helper Scripts

These wrappers are available in `scripts/`:

```bash
cd /opt/automaton
./scripts/prod-redeploy-api.sh
./scripts/prod-redeploy-website.sh
./scripts/prod-redeploy-website.sh --with-cli-assets
./scripts/prod-redeploy-all.sh
./scripts/prod-redeploy-all.sh --with-cli-assets
```

Note: `website-cli-assets` is a build-only helper image. It is not a long-running container, so you build it when needed but you do not `up -d` it as part of the stack.

## Logs

Follow one service:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml logs -f api
docker compose -f docker-compose.prod.yml logs -f studio
docker compose -f docker-compose.prod.yml logs -f website
docker compose -f docker-compose.prod.yml logs -f traefik
docker compose -f docker-compose.prod.yml logs -f mysql
docker compose -f docker-compose.prod.yml logs -f socket-proxy
```

Check the resolved compose config:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml config
docker compose -f docker-compose.prod.yml --profile build-assets config
```
