# Production Docker Cheat Sheet

Use these commands from the production server after pulling the latest code into `/opt/automaton`.

## Blue/Green Basics

Traefik now serves `api`, `studio`, and `website` through a generated file-provider config:

- state file: `docker/traefik/dynamic/blue-green-state.env`
- routes file: `docker/traefik/dynamic/blue-green-routes.yml`

Each public service has two Compose services:

- `api-blue` / `api-green`
- `studio-blue` / `studio-green`
- `website-blue` / `website-green`

The deploy scripts build the inactive color, wait for health checks to pass, switch Traefik to that color, and then stop the old color unless you pass `--keep-previous`.

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
./scripts/prod-redeploy-all.sh --with-cli-assets
```

Keep the previous color running for rollback:

```bash
cd /opt/automaton
./scripts/prod-redeploy-all.sh --keep-previous
```

## Single Service Redeploy

API:

```bash
cd /opt/automaton
./scripts/prod-redeploy-api.sh
```

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
./scripts/prod-redeploy-website.sh --with-cli-assets
```

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

## First-Time Bootstrap

On a fresh server:

```bash
cd /opt/automaton
cp .env.prod.example .env
# edit .env and /opt/automaton/config/appsettings.Production.json
chmod +x ./scripts/prod-*.sh
./scripts/prod-redeploy-all.sh
```

`prod-redeploy-all.sh` bootstraps the generated Traefik routes file, starts `mysql`, `socket-proxy`, and `traefik`, then deploys the first live color for API, Studio, and website.

## Infrastructure Services

These services do not rebuild from local source in this repo:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml up -d mysql socket-proxy traefik
```

Pull newer upstream images first if needed:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml pull mysql socket-proxy traefik
docker compose -f docker-compose.prod.yml up -d mysql socket-proxy traefik
```

## Website CLI Downloads

Rebuild the downloadable CLI bundles when `src/Automaton.Cli` or `shared/ai-guidance` changed:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml --profile build-assets build website-cli-assets
```

Then redeploy the website:

```bash
cd /opt/automaton
./scripts/prod-redeploy-website.sh --with-cli-assets
```

Rule of thumb:

- CLI code changed: rebuild `website-cli-assets`
- Shared AI guidance changed: rebuild `website-cli-assets`
- Website UI or docs changed without CLI asset changes: redeploy `website` only

## Logs

Follow one color:

```bash
cd /opt/automaton
docker compose -f docker-compose.prod.yml logs -f api-blue
docker compose -f docker-compose.prod.yml logs -f api-green
docker compose -f docker-compose.prod.yml logs -f studio-blue
docker compose -f docker-compose.prod.yml logs -f studio-green
docker compose -f docker-compose.prod.yml logs -f website-blue
docker compose -f docker-compose.prod.yml logs -f website-green
docker compose -f docker-compose.prod.yml logs -f traefik
```

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
docker compose -f docker-compose.prod.yml --profile build-assets config
```

## Notes

- This is safer than `docker compose up -d --build website` because Traefik keeps serving the currently live color until the new color passes health checks.
- Cloudflare should no longer need the DNS proxy off/on workaround in normal deploys, because the hostname never points at a half-restarted backend.
- API blue/green assumes database changes are backward-compatible for the duration of the cutover. Avoid schema changes where the old API crashes immediately after the migration is applied.
