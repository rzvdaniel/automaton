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

## Permitted cleanup

Before cleanup, confirm that active containers and both the current and immediately previous immutable revision-tagged images are preserved. Cleanup must not remove images tagged with either revision.

After confirming that active containers and rollback capacity are preserved, the bounded cleanup is:

```bash
docker builder prune -af --keep-storage 4GB
docker image prune -f
```

Run the inspection commands again afterward and confirm that active services remain healthy. Never prune Docker volumes, use `docker system prune -a`, or use `docker image prune -a` automatically. Preserve blue/green rollback images and all production state. If an item’s safety is unclear, keep it and report it.

## Broader cleanup

For cleanup beyond routine cache/dangling-image pruning, use the user's existing authorization for the named categories and retention window. A release request alone does not authorize deletion of backups or incremental recovery history. Explain the concrete recovery consequence before requesting any missing authorization; do not ask again for already-authorized cleanup. Describing an environment as development does not make its live data disposable.

- **Obsolete tagged images:** inventory all containers, including stopped containers, image IDs/tags/digests, blue-green current/previous revision state and configured Runner artifact references. Remove individually verified unused historical references with `docker image rm`, without force. Preserve both immediate rollback revisions, active images and pinned artifacts needed for provisioning. Keep unattributed recovery artifacts until a replacement is verified; removing a tag may reclaim little because layers are shared.
- **System journals:** use `journalctl --vacuum-size` or `--vacuum-time` with an agreed bound to remove archived journals. Active journal files remain; do not truncate active container/application logs or delete audit records as disk maintenance.
- **MySQL binary logs:** inspect `SHOW BINARY LOGS`, expiry settings, replication topology (including disconnected replicas) and backup/point-in-time recovery dependencies. Purge only an authorized cutoff using MySQL's [PURGE BINARY LOGS](https://dev.mysql.com/doc/refman/8.0/en/purge-binary-logs.html); never unlink database files. If discarding all closed history is authorized and dependencies are clear, rotate with `FLUSH BINARY LOGS`, read the new active filename, then purge up to that filename, retaining the active log. Change persistent expiry only within the authorized retention policy and report it; use syntax supported by the deployed MySQL version. Do not reset GTID/binlog state or disable logging for routine cleanup.
- **Historical backups:** delete only explicitly authorized backup roots/files after checking resolved paths. Do not follow symlinks into live certificates or other state; unlink backup symlinks themselves. Preserve live volumes, current configuration/secrets and unrelated data. Report which restore points were removed.

After cleanup, report bytes reclaimed and remaining free space, retained rollback artifacts, changed retention settings and any recovery history removed. Run production health verification. Record significant recovery-policy changes in the operational evidence; do not infer milestone acceptance from successful cleanup.

For the broader production workflow, read the [production cheat sheet](../../../docs/prod/production-docker-cheat-sheet.md).
