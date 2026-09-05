---
name: production-docker-maintenance
description: Safely reclaim Docker disk space before or after Automaton production builds and deployments.
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

## Permitted cleanup

After confirming that active containers and rollback capacity are preserved, the bounded cleanup is:

```bash
docker builder prune -af --keep-storage 4GB
docker image prune -f
```

Run the inspection commands again afterward and confirm that active services remain healthy. Never prune Docker volumes, use `docker system prune -a`, or use `docker image prune -a` automatically. Preserve blue/green rollback images and all production state. If an item’s safety is unclear, keep it and report it.

For the broader production workflow, read the [production cheat sheet](../../../docs/prod/production-docker-cheat-sheet.md).
