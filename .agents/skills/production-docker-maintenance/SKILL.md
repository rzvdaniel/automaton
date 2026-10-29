---
name: production-docker-maintenance
description: Inspect production host storage and safely reclaim Docker artifacts, archived logs and authorized recovery history before or after Automaton deployments.
license: Apache-2.0
---

# Production Docker maintenance

Use this skill when inspecting or reclaiming Docker disk space on Automaton production. For deployment, cutover, or rollback operations, also follow [production-deployment](../production-deployment/SKILL.md).

## Inspect first

Run read-only checks before cleanup:

```bash
df -h /
docker system df
docker ps
```

Do not start a production build with less than 10 GB free. If space is unexpectedly low, identify the cause and report it before deleting anything.

Use `docker system df -v` to distinguish shared image layers, unique image bytes, build cache and volumes. Inspect host directory sizes and `sudo journalctl --disk-usage` when Docker does not explain the footprint. A large MySQL volume may contain binary logs as well as table/index data; compare file sizes with `information_schema.tables` totals without reading private payloads or exposing credentials.

Inspect every filesystem involved, not just Docker's configured root: use `docker info` to locate that root, `findmnt -T <path>` and `df -hT <path>` to identify its backing filesystem, and bounded `du -x` scans to attribute physical storage. Docker may also use an active `/var/lib/containerd` store on a different disk. Check daemon arguments, containerd namespaces and live tasks before classifying an old-looking store as abandoned. Never delete containerd content/snapshots manually. Image sizes, shared layers and build-cache accounting overlap; do not add their reported totals as independent physical usage.

Include system journals, archived logs, stopped containers, build caches and task-owned staging/recovery archives in the inventory. For MySQL, report table/index size separately from binary-log bytes and inspect expiry, replica configuration and backup dependencies. Absence of connected replicas alone does not establish that recovery logs are disposable. Do not print secrets from cron jobs, service configuration or environment files while investigating backups.

## Known storage locations on this host

These are inventory hints from the production host, not fixed cleanup targets; recheck current mounts and files each time.

- **MySQL volume:** inspect the volume mounted at `/var/lib/mysql` with `docker inspect automaton-prod-mysql` and `du`, then compare its contents with table/index totals. The volume can be dominated by `binlog.*`, `undo_*`, and redo files even when the `automaton` tables are small. This host uses row-based binlogging; large row deletes can themselves produce large binlogs, so table cleanup may not immediately free the same amount on the host. Check `binlog_expire_logs_seconds` and `binlog_expire_logs_auto_purge`; auto-expiry is age-based, and MySQL removes expired logs at startup or when the binary log is flushed, not exactly at the expiry second. Keep the active log. See [MySQL log-file maintenance](https://dev.mysql.com/doc/refman/8.0/en/log-file-maintenance.html).
- **Worker file logs:** inspect the named volume mounted at `/app/logs` in `automaton-prod-worker`, including largest dated files, not only Docker's container log sizes. The Worker config defaults to 14 retained files and 10 MiB size-based rolling, but this host had dated files of hundreds of MiB. Verify actual rotation behavior and keep the current file; remove only closed, dated logs when authorized.
- **Temporary test artifacts:** inspect large files in `/tmp` and correlate runner test archives/images with `docker ps -a` and image IDs before removal. This host had a 452 MiB dated runner test image archive plus two unused `m1-test` images. An archive and its Docker image are separate storage; removing one does not remove the other.
- **Containerd image store:** `/var/lib/containerd` can be much larger than `docker system df` alone suggests because it holds content blobs and unpacked snapshots. Use Docker/containerd tools to identify unused images; never remove files from this directory manually.

## Permitted cleanup

Before cleanup, confirm that active containers and both the current and immediately previous immutable revision-tagged images are preserved. Cleanup must not remove images tagged with either revision.

After confirming that active containers and rollback capacity are preserved, the bounded cleanup is:

```bash
docker builder prune -af --keep-storage 2GB
docker image prune -f
```

Run the inspection commands again afterward and confirm that active services remain healthy. Never prune Docker volumes, use `docker system prune -a`, or use `docker image prune -a` automatically. Preserve blue/green rollback images and all production state. If an item’s safety is unclear, keep it and report it.

The 2 GB cache target is a retention budget, not a minimum required by Docker. Inspect the selected builder first; `docker builder prune` does not maintain an unrelated BuildKit daemon. Prefer its native configured GC for ongoing retention. A prune can reclaim zero bytes; verify actual filesystem free space afterward rather than promising Docker's estimated reclaimable total. Journal vacuuming is a one-time cleanup unless retention configuration is separately changed.

Use the [persisted host policies](../../../docs/prod/production-docker-cheat-sheet.md#disk-cleanup) for ongoing maintenance: per-filesystem alerts/admission, native cache GC, journald limits, container/file-log rotation and explicit image protection. `scripts/host-storage.sh maintain` only removes unused tags in allow-listed repositories, protects container image IDs and deployment revision state, and fails closed on missing protected references. Run it under the same deployment lock; never replace it with a blanket image or volume prune. Source changes do not authorize installing policies or restarting existing hosts.

## Broader cleanup

For cleanup beyond routine cache/dangling-image pruning, use the user's existing authorization for the named categories and retention window. A release request alone does not authorize deletion of backups or incremental recovery history. Explain the concrete recovery consequence before requesting any missing authorization; do not ask again for already-authorized cleanup. Describing an environment as development does not make its live data disposable.

- **Obsolete tagged images:** inventory all containers, including stopped containers, image IDs/tags/digests, blue-green current/previous revision state and configured Runner artifact references. Remove individually verified unused historical references with `docker image rm`, without force. Preserve both immediate rollback revisions, active images and pinned artifacts needed for provisioning. Keep unattributed recovery artifacts until a replacement is verified; removing a tag may reclaim little because layers are shared.
- **System journals:** use `journalctl --vacuum-size` or `--vacuum-time` with an agreed bound to remove archived journals. Active journal files remain; do not truncate active container/application logs or delete audit records as disk maintenance.
- **MySQL binary logs:** inspect `SHOW BINARY LOGS`, expiry settings, replication topology (including disconnected replicas) and backup/point-in-time recovery dependencies. Purge only an authorized cutoff using MySQL's [PURGE BINARY LOGS](https://dev.mysql.com/doc/refman/8.0/en/purge-binary-logs.html); never unlink database files. If discarding all closed history is authorized and dependencies are clear, rotate with `FLUSH BINARY LOGS`, read the new active filename, then purge up to that filename, retaining the active log. Change persistent expiry only within the authorized retention policy and report it; use syntax supported by the deployed MySQL version. Do not reset GTID/binlog state or disable logging for routine cleanup.
- **Historical backups:** delete only explicitly authorized backup roots/files after checking resolved paths. Do not follow symlinks into live certificates or other state; unlink backup symlinks themselves. Preserve live volumes, current configuration/secrets and unrelated data. Report which restore points were removed.

Before deleting staging or migration archives, inspect their purpose and compare their exact paths with container mounts and active configuration references. An old task directory may still contain mounted TLS files, initialization scripts or authentication keys. Keep those live files even if adjacent archives are obsolete. Never infer that all similarly named directories are disposable from authorization to remove one recovery copy.

After cleanup, report bytes reclaimed and remaining free space, retained rollback artifacts, changed retention settings and any recovery history removed. Verify the installer-managed services and their public/origin routes. Record significant recovery-policy changes in the operational evidence; do not infer milestone acceptance from successful cleanup.

For the broader production workflow, read the [production cheat sheet](../../../docs/prod/production-docker-cheat-sheet.md).
