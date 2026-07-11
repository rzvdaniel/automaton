# Managed Runtime Local Validation

This guide adds a production-shaped local edge for managed runtime validation:

- browser -> Traefik
- Traefik -> managed runner frontend/backend gateway
- API writes Traefik dynamic route files

It is intentionally separate from `docker-compose.local.yml` so you can keep:

- MySQL
- RabbitMQ
- runner container
- API on `dotnet watch`

and only add the missing runtime edge.

## What this validates

Use this setup when you want to validate:

- `/a/{appId}/...` static frontend serving
- `/a/{appId}/api/controllers/...` runner backend execution
- `/a/{appId}/api/flows/{flowIdentifier}/execute` runner flow execution
- upload and download helpers
- API participation through the normal API hostname
- Traefik dynamic route generation

## Prerequisites

You need:

1. The existing local network and runner container from:

```powershell
docker compose -f docker-compose.local.yml up -d mysql rabbitmq runner
```

2. Local hostnames in your hosts file:

```txt
127.0.0.1 api.automaton.test
127.0.0.1 apps.automaton.test
127.0.0.1 studio.automaton.test
```

3. A local TLS certificate for `apps.automaton.test`.

Recommended with `mkcert`:

```powershell
mkcert apps.automaton.test
```

Place the generated files at:

```txt
.local-dev/managed-runtime/certs/apps.automaton.test.pem
.local-dev/managed-runtime/certs/apps.automaton.test-key.pem
```

4. Writable local directories:

```powershell
New-Item -ItemType Directory -Force -Path .local-dev\managed-runtime\traefik-dynamic | Out-Null
New-Item -ItemType Directory -Force -Path .local-dev\managed-runtime\certs | Out-Null
```

## API configuration

Run the API on your host with `dotnet watch` and set these overrides in the same shell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:PublishedApps__ApiBaseUrl = "https://api.automaton.test:7091"
$env:PublishedApps__PublicBaseUrl = "https://apps.automaton.test:8443"
$env:PublishedApps__RuntimeProxyAuthKey = "local-managed-runtime-secret"
$env:PublishedApps__TraefikDynamicConfigRoot = (Resolve-Path ".local-dev\managed-runtime\traefik-dynamic").Path

dotnet watch --project src\Automaton.Api
```

Notes:

- `https://apps.automaton.test:8443` is the local Traefik TLS entrypoint.
- The runtime cookie is always `Secure`, so browser validation must use HTTPS.

## Edge overlay

Start the local managed-runtime edge:

```powershell
$env:MANAGED_RUNTIME_PROXY_AUTH_KEY = "local-managed-runtime-secret"
docker compose -f docker-compose.managed-local.yml up -d
```

This starts `automaton-local-managed-traefik`.

It joins the existing `automaton-local` Docker network so Traefik can route to the runner container by container DNS name.

## Runner reachability

The runner FastAPI must be reachable from Traefik at container scope, not just `127.0.0.1`.

The runner container entrypoint now defaults:

```txt
RUNNER_FASTAPI_HOST=0.0.0.0
```

That keeps manual host-side development safe by default while making the containerized runtime reachable to Traefik.

## Route target requirement

Traefik routes are generated from the hosted runner runtime endpoint, not from the live runner connection directly.

For local validation, the hosted runner record associated with your app's `ExecutionRunnerId` must have:

```txt
RuntimeEndpoint = http://automaton-local-runner:8001
```

If that endpoint is missing or points somewhere else, the route file may exist but backend runtime calls will still fail.

## Validation checklist

After deploying an app with a private frontend and assigned runner:

1. Confirm static frontend export exists:

```powershell
Get-ChildItem .local-dev\managed-runtime\static-runtime\apps\<appId>
```

2. Confirm Traefik route file exists:

```powershell
Get-Content .local-dev\managed-runtime\traefik-dynamic\managed-runtime-routes.yml
```

3. Launch runtime from Studio and confirm the browser lands on:

```txt
https://apps.automaton.test:8443/a/{appId}/
```

4. Verify:

- frontend loads
- controller execution works through `/a/{appId}/api/controllers/...`
- flow execution works through `/a/{appId}/api/flows/{flowIdentifier}/execute`
- upload and download helpers work

5. Verify failure paths:

- wrong app id denied
- missing runtime cookie denied
- unknown flow denied
- missing artifacts fail safely

## Useful diagnostics

Nginx config and logs:

```powershell
docker exec automaton-local-managed-nginx nginx -t
docker logs --tail 200 automaton-local-managed-nginx
```

Traefik ping and logs:

```powershell
Invoke-WebRequest http://localhost:8082/ping
docker logs --tail 200 automaton-local-managed-traefik
```

Runner logs:

```powershell
docker logs --tail 200 automaton-local-runner
```

The key runtime check is that browser traffic goes:

- browser -> nginx for `/a/{appId}/...`
- nginx -> API only for `/internal/runtime/auth`
- nginx -> Traefik -> runner for `/a/{appId}/api/...`

`Automaton.Api` should not be in the runtime backend data path.
