# Runtime-Local Stack

This stack runs the local Automaton environment in containers behind a single nginx TLS edge, closer to the production runtime shape than the host-mixed local setup.

Use this page as the local managed-runtime cheat sheet.

## What it runs

- `nginx`
- `traefik`
- `api`
- `worker`
- `studio`
- `runner`
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

Add these entries to your Windows hosts file:

```txt
127.0.0.1 api.automaton.test
127.0.0.1 studio.automaton.test
127.0.0.1 apps.automaton.test
127.0.0.1 website.automaton.test
```

Typical file path:

```txt
C:\Windows\System32\drivers\etc\hosts
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

If `mkcert` is not installed yet:

```powershell
choco install mkcert
mkcert -install
```

Expected output files:

```txt
.local-runtime/certs/automaton.test.pem
.local-runtime/certs/automaton.test-key.pem
```

## From scratch

If you want a clean restart of the runtime-local stack:

1. Stop and remove the old runtime-local containers.
2. Keep or recreate the local certs under `.local-runtime/certs/`.
3. Restore the MySQL dump if you want your previous local data back.
4. Start the stack with the bootstrap script.

If you are intentionally wiping the runtime-local stack first:

```powershell
docker compose -f docker-compose.runtime-local.yml down -v
```

This removes the runtime-local containers and Docker volumes created by that compose file.
It does not remove your SQL dump file.

## Clean-machine bootstrap

On Windows, the recommended entry point is:

```powershell
.\scripts\setup-local-development.ps1
```

The bootstrap script is idempotent and performs the local prerequisites, hosts-file,
certificate, full-stack build/start, and readiness checks. It builds API, Worker,
Studio, Website, and Runner so it also works on a machine without existing local
images.

Useful options:

```powershell
.\scripts\setup-local-development.ps1 -SkipBuild
.\scripts\setup-local-development.ps1 -NoHostsFileChanges
.\scripts\setup-local-development.ps1 -ProvisionTestRunner
.\scripts\setup-local-development.ps1 -Reset
```

`-Reset` is destructive: it removes the runtime-local containers and volumes and
any managed hosted-runner containers. It is never used during a normal setup.
`-ProvisionTestRunner` prompts for an existing local administrator account, assigns
the active default plan through the API, and waits for the real hosted-runner
provisioning flow to reach `running`. The smoke-test application deployment is
still performed separately through the normal CLI workflow.

## Managed runners

The runtime-local stack now enables managed hosted runner provisioning.

That means a backend-capable app can follow the normal product path:

- deploy from CLI or Studio
- API requests managed runtime preparation
- worker provisions a hosted runner container through the local Docker socket
- runner connects back to API
- app backend deploy proceeds on retry

The stack still does not start a permanent shared runner container up front. Hosted runners are created on demand per user.

The local hosted-runner settings in `docker-compose.runtime-local.yml`:

- `HostedRunners__LocalRunnerHostCpuCores`
- `HostedRunners__LocalRunnerHostMemoryMb`
- `HostedRunners__LocalRunnerHostDiskMb`

are scheduler capacity budgets for the auto-seeded local `RunnerHost`.

They are not auto-detected machine specs. They simply define how much hosted-runner capacity the local scheduler should consider available on the synthetic `runtime-local-docker` host. Keep them high enough for normal local testing, or reduce them if you want to exercise capacity limits more aggressively.

## Start or redeploy

Use:

```powershell
.\scripts\local-runtime-redeploy.ps1
```

Useful options:

```powershell
.\scripts\local-runtime-redeploy.ps1 -NoBuild
.\scripts\local-runtime-redeploy.ps1 -Services api,worker,studio
.\scripts\local-runtime-redeploy.ps1 -Services api
```

What the script does:

- ensures `.local-runtime/certs`, `.local-runtime/static-runtime`, and `.local-runtime/traefik-dynamic` exist
- checks that the local TLS cert files exist
- defaults `LOCAL_RUNTIME_MYSQL_PORT` to `3311` when it is not set
- builds selected services sequentially unless `-NoBuild` is passed
- starts the runtime-local compose stack

Default build services:

- `api`
- `worker`
- `studio`
- `runner`

The `-Services` parameter only affects which services are built. The script still starts the full runtime-local stack with `docker compose up -d --remove-orphans`.

The script now prints the effective MySQL port before startup, for example:

```txt
[INFO] Using LOCAL_RUNTIME_MYSQL_PORT=3311
```

That matters because a local Windows MySQL service can already be using `3307`.

## Faster inner loop

Use the full script when:

- you changed compose wiring
- you changed certs, volumes, networks, or local runtime bootstrap behavior
- you want a full stack rebuild/reset

For normal code-only changes in API, Worker, or Studio, use the narrower compose update instead:

```powershell
docker compose -f docker-compose.runtime-local.yml up -d --build api worker studio
```

Equivalent two-step form:

```powershell
docker compose -f docker-compose.runtime-local.yml build api worker studio
docker compose -f docker-compose.runtime-local.yml up -d api worker studio
```

This is the recommended day-to-day development loop when you only changed:

- `src/Automaton.Api`
- `src/Automaton.Worker`
- `src/automaton-studio-vue`

If you changed managed runner container code under `src/Automaton.Runner`, include `runner` in the build set:

```powershell
docker compose -f docker-compose.runtime-local.yml build runner api worker
docker compose -f docker-compose.runtime-local.yml up -d api worker
```

Important:

- build the `runner` image when `src/Automaton.Runner` changes
- do not start the shared `runner` service for the managed-runtime path
- managed provisioning creates runner containers on demand from the prebuilt `automaton-runtime-local-runner:latest` image

It avoids unnecessary restarts of:

- `mysql`
- `rabbitmq`
- `smtp4dev`
- `nginx`
- `traefik`
- `website`

## Local endpoints

After a successful start:

- Studio: [https://studio.automaton.test:8443](https://studio.automaton.test:8443)
- API health: [https://api.automaton.test:8443/healthz](https://api.automaton.test:8443/healthz)
- Apps health: [https://apps.automaton.test:8443/healthz](https://apps.automaton.test:8443/healthz)
- Website: [https://website.automaton.test:8443](https://website.automaton.test:8443)
- RabbitMQ management: [http://localhost:15673](http://localhost:15673)
- smtp4dev: [http://localhost:3001](http://localhost:3001)
- Traefik ping: [http://localhost:8082/ping](http://localhost:8082/ping)

## Restore a MySQL dump

If you already created a SQL dump with `mysqldump`, that dump contains both schema and data.

Recommended clean restore flow:

1. Start the runtime-local stack.
2. Drop and recreate the `automaton` database in the runtime-local MySQL container.
3. Import the dump.
4. Restart API and worker.

Drop and recreate the database:

```powershell
docker exec -i automaton-runtime-local-mysql mysql -u root -pAutomatonRoot123! -e "DROP DATABASE IF EXISTS automaton; CREATE DATABASE automaton;"
```

Import the dump:

```powershell
Get-Content ".artifacts\db-backups\automaton-before-runtime-local.sql" | docker exec -i automaton-runtime-local-mysql mysql -u root -pAutomatonRoot123! automaton
```

Restart API and worker after restore:

```powershell
docker restart automaton-runtime-local-api automaton-runtime-local-worker
```

Optional sanity check:

```powershell
docker exec -i automaton-runtime-local-mysql mysql -u root -pAutomatonRoot123! -D automaton -e "SHOW TABLES;"
```

If your dump file has a different name, substitute the actual path.

## MySQL Workbench

To connect MySQL Workbench to the runtime-local MySQL container, create a connection with:

- Connection Name: `AutomatonRuntimeLocal`
- Hostname: `127.0.0.1`
- Port: `3311`
- Username: `root`
- Password: `AutomatonRoot123!`

After saving the connection, open it and refresh the `Schemas` panel. You should see the `automaton` database after restore.

Important:

- the runtime-local MySQL container is exposed on `3311`
- an older local or Docker MySQL connection on `3306` is not the same database

### Import with Workbench

If you prefer restoring the dump through MySQL Workbench instead of PowerShell:

1. Open the `AutomatonRuntimeLocal` connection.
2. Go to `Administration` -> `Data Import/Restore`.
3. Choose `Import from Self-Contained File`.
4. Select:

   ```txt
   C:\Work\automaton\.artifacts\db-backups\automaton-before-runtime-local.sql
   ```

5. Set the target schema to `automaton`.
6. Start Import.

## Typical bootstrap flow

For a clean machine or a fully reset local runtime:

1. Add the `automaton.test` host entries.
2. Generate the local SAN certificate into `.local-runtime/certs/`.
3. Run:

   ```powershell
   .\scripts\local-runtime-redeploy.ps1
   ```

4. Restore the MySQL dump if needed.
5. Log into Studio at [https://studio.automaton.test:8443](https://studio.automaton.test:8443).
6. Deploy a backend-capable app. The first deploy may return a preparing/provisioning state while the hosted runner container is created.

## Script usage patterns

Full local runtime start with the default development build set:

```powershell
.\scripts\local-runtime-redeploy.ps1
```

Restart the stack without rebuilding:

```powershell
.\scripts\local-runtime-redeploy.ps1 -NoBuild
```

Rebuild only API and Worker, then bring the full stack up:

```powershell
.\scripts\local-runtime-redeploy.ps1 -Services api,worker
```

Rebuild only Studio, then bring the full stack up:

```powershell
.\scripts\local-runtime-redeploy.ps1 -Services studio
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

Important local folders:

```txt
.local-runtime/certs
.local-runtime/static-runtime
.local-runtime/traefik-dynamic
```

## Notes

- This stack is intentionally closer to production networking than `docker-compose.local.yml`.
- It does not attempt local blue/green rollout behavior.
- It keeps a dedicated namespace instead of reusing the older local stack containers.
- The compose file still contains a runner service profile for explicit local experiments, but managed runtime no longer depends on that profile.
- Frontend app traffic is expected to go through `https://apps.automaton.test:8443`, not through `localhost`.
- API traffic for Studio is expected to go through `https://api.automaton.test:8443`, not through `https://localhost:7091`.
