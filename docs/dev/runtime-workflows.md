# Local runtime workflows

Use one of these modes deliberately.

| Workflow | API | Studio | Worker | Traefik | Managed runner |
| --- | --- | --- | --- | --- | --- |
| Full runtime-local | Container | Container | Container | Container | Dynamically provisioned |
| Host development | Host | Host | Host or container | Edge overlay | Dynamically provisioned |
| Direct runner debugging | Optional | Optional | Optional | Not required | Mapped runner container |

## Full end-to-end validation

```powershell
docker compose -f docker-compose.runtime-local.yml up --build
```

This is the authoritative local path for Traefik TLS on `:8443`, API, Studio, Website, managed-runner provisioning, generated runner routes, slug routes, runner frontend/API serving, SignalR, and CLI deployment.

## Fast host-development loop

Prepare infrastructure and the managed-runtime edge:

```powershell
.\scripts\start-managed-runtime-host-dev.ps1
```

Run the API and Studio from the host in separate terminals:

```powershell
dotnet watch --project src\Automaton.Api
```

```powershell
cd src\automaton-studio-vue
npm run dev
```

This mode is optimized for API, Studio, routing, orchestration, database, and CLI work. It is not a substitute for the full runtime-local validation path.

## Managed-runtime edge overlay

`docker-compose.managed-local.yml` is infrastructure support for host development only. It provides Traefik and runner-route forwarding; it does not provide API, Studio, Website, MySQL, or RabbitMQ.

The dynamically provisioned managed runner is the runtime under test. Do not use a legacy shared runner as a substitute when validating hosted-runner provisioning.

## Direct runner debugging

Use the runner's mapped local runtime endpoint directly when debugging runner serving or backend lifecycle. Traefik is not required for this path.
