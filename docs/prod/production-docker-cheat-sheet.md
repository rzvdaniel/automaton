# Production operations

Production uses the [versioned server installer and updater](install-automaton.md). The source blue/green deployment scripts have been removed.

## Updates and status

```bash
sudo bash /opt/automaton-server/update.sh
sudo bash /opt/automaton-server/install.sh --status
```

The updater verifies the release checksum, backs up configuration/database/app data/key rings, and updates API, Worker and Studio. Select a release with `--version server-vX.Y.Z`. Database/broker upgrades require separate review. Fresh installation does not migrate or overwrite an existing configuration.

For an adopted installation:

```bash
cd /opt/automaton-server
sudo docker compose --env-file release.env --env-file .env -f compose.yaml -f compose.override.yaml ps
sudo docker compose --env-file release.env --env-file .env -f compose.yaml -f compose.override.yaml logs --tail 100 api worker traefik
```

Omit the override argument where none exists. Never share resolved Compose output or raw secrets. Preserve signing/encryption keys and Data Protection volumes with the database for recovery.

The marketing website is separate from the server bundle. The adopted host retains its `website-green` container on the original network. If using the old Compose file for that website, select only that service with `--no-deps --no-build`; never start retired infrastructure services. Their containers and deployment scripts have been removed.

## Routing and verification

Traefik terminates HTTPS and the TLS Runner broker connection. The API writes registered app routes into its shared dynamic directory. Static sites share a Machine Management Runner static server; dynamic APIs use dedicated bounded containers. A wildcard certificate does not need a wildcard API fallback: deleted or unknown app hostnames return 404.

Verify origin routes with `curl --resolve` and public hostnames after changes. Check API and apps-host `/healthz`, Studio, installation status, and any separate website. Verify the broker certificate/hostname and an AMQP response on its configured address. Container health or a Traefik ping alone is insufficient.

## Storage maintenance

```bash
df -hT
docker system df -v
docker ps -a
docker info --format '{{.DockerRootDir}}'
sudo journalctl --disk-usage
```

Inspect all relevant filesystems, including Docker/containerd, persistent volumes, logs and backups. Docker layer/cache totals overlap; never remove containerd snapshots manually. Replacement/build admission requires at least 10 GiB and 15% free space. Expand storage if live state and retained artifacts cannot fit with that headroom.

`host-storage.sh` and `docker/host-maintenance/` provide capacity inspection and opt-in retention policies. Applying host policies or restarting Docker is separate from a source merge.

- Preserve active images, configured Runner artifacts and explicitly retained rollback references. Image maintenance uses a protected allow-list and fails closed if a reference is missing. Historical blue/green state may still protect images but is not the active deployment state.
- Configure the actual builder's native GC with a 2 GB cache target. After confirming no build is active, bounded one-time cleanup can use `docker builder prune -af --keep-storage 2GB`.
- Container stdout, application file logs and audit records have separate retention. Do not delete live application data or audit records as log cleanup.
- The installer defaults to seven-day MySQL binlog expiry. Verify actual settings and backup/replica dependencies. Purge only authorized history using MySQL commands; never unlink database files.
- Compare exact resolved backup/staging paths with container mounts before removal. Do not follow backup symlinks into live settings or certificate stores.
- Never use blanket volume pruning, `docker system prune -a`, or automatic `docker image prune -a`.

Manual maintenance must not overlap an updater. Coordinate the installation and deployment locks on adopted hosts. Keep automatic image cleanup disabled until its protected references and lock coordination are correct. Capacity monitoring and native retention can remain active independently.

Future updater runs create new protected recovery backups. Retain them according to the operator's policy and copy them to encrypted off-host storage when configured. Explicit removal ends recovery from those copies; it does not delete live volumes.

## Acceptance and cleanup evidence

On 28 October 2026, the control-plane server was adopted and updated through the normal updater to `server-v0.1.6`. Infrastructure versions, credentials, broker node identity, app/key/certificate volumes, networks and private broker bindings were preserved.

On 29 October, a new Machine Management Runner enrolled normally. Two CLI-created static sites shared one container; deleting one preserved its sibling and the same container. A Python API task was created, marked complete and stayed complete after redeploy. A deployed Action returned the expected result. App deletion removed dedicated containers, source/runtime/data directories, static output and Nginx configurations. Registered Traefik routes disappeared, and a fresh publish/delete check verified 404 after removing the wildcard API fallback.

After acceptance, the operator authorized removing migration/update backups, retired source deployment scripts and unused containers. Live volumes and active services were preserved. The management Runner remains available for new apps. Original rollback containers and those local recovery backups are no longer available. This does not establish disaster recovery or performance at thousands of apps.
