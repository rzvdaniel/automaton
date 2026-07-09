# Runtime-Local Stack

This stack runs the local Automaton environment in containers behind a single nginx TLS edge, closer to the production runtime shape than the host-mixed local setup.

## What it runs

- `nginx`
- `traefik`
- `api`
- `worker`
- `studio`
- `website`
- `mysql`
- `rabbitmq`
- `smtp4dev`

The intended browser-facing endpoints are:

- `https://studio.automaton.test:8443`
- `https://api.automaton.test:8443`
- `https://apps.automaton.test:8443`
- `https://website.automaton.test:8443`

## Why use it

Use this stack when you want:

- managed runtime auth/cookie behavior that matches the deployed shape
- nginx -> API `auth_request` validation
- nginx -> Traefik -> runner backend routing
- Studio and API on sibling domains instead of `localhost`
- runtime validation without mixing host `dotnet watch` and container proxies

## Hosts file

Add these entries:

```txt
127.0.0.1 api.automaton.test
127.0.0.1 studio.automaton.test
127.0.0.1 apps.automaton.test
127.0.0.1 website.automaton.test
```

## Certificate

Generate one SAN certificate that covers all local hosts:

```powershell
New-Item -ItemType Directory -Force -Path .local-runtime\certs | Out-Null

mkcert `
  -cert-file ".local-runtime\certs\automaton.test.pem" `
  -key-file ".local-runtime\certs\automaton.test-key.pem" `
  api.automaton.test studio.automaton.test apps.automaton.test website.automaton.test
```

## Runner note

This bootstrap script does not start the runner container.

That is intentional. Managed runtime backend routing requires more than a running container:

- a `Runner` row
- a `HostedRunner` row
- a usable runtime endpoint
- app assignment state such as `ExecutionRunnerId`

So the base stack brings up the platform services only. Runner setup should happen separately through the real product flow or an explicit local runner setup step.

## Start or redeploy

Use:

```powershell
.\scripts\local-runtime-redeploy.ps1
```

Useful options:

```powershell
.\scripts\local-runtime-redeploy.ps1 -NoBuild
```

## Compose file

The stack definition lives in:

```txt
docker-compose.runtime-local.yml
```

## Runtime state

Local runtime state lives under:

```txt
.local-runtime/
```

and in Docker volumes named with the `runtime-local` prefix.

## Notes

- This stack is intentionally closer to production networking than `docker-compose.local.yml`.
- It does not attempt local blue/green rollout behavior.
- It keeps a dedicated namespace instead of reusing the older local stack containers.
- The compose file still contains a runner service profile for explicit local experiments, but the bootstrap script does not use it.
