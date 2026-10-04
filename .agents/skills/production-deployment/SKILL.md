---
name: production-deployment
description: Use when changing or operating Automaton production deployment, Runner image release/publishing, production scripts, Compose, Traefik routes, certificates, health checks, blue-green cutovers, or rollback.
license: Apache-2.0
---

# Production deployment

Use this skill for production operations and changes to production deployment machinery. A live deployment requires an explicit user request.

## Current model

API, Studio, and Website use blue-green services. The Worker is updated in place. Traefik serves public traffic and reads generated dynamic configuration. Hosted app traffic goes directly from the runtime edge to Runner endpoints.

Read the current scripts and [the production cheat sheet](../../../docs/prod/production-docker-cheat-sheet.md) before changing the workflow:

- `scripts/prod-redeploy-api.sh`
- `scripts/prod-redeploy-studio.sh`
- `scripts/prod-redeploy-website.sh`
- `scripts/prod-redeploy-worker.sh`
- `scripts/prod-adopt-image-revisions.sh`
- `scripts/prod-verify.sh`

## Runner image release authority

Production Runner Docker images are built and published by the repository's
Azure DevOps pipeline, `azure-pipelines-runner-docker.yml`.

Do not create a parallel production Runner build or publishing path on a
production host or developer machine. For changes under `src/Automaton.Runner/`:

1. Validate the change locally as appropriate.
2. Merge it to the intended release revision.
3. Let the Azure DevOps Runner pipeline build and publish the production image.
4. Verify the exact published image digest and image-level smoke checks.
5. Deploy or install Runners from that CI-produced artifact.

A local Docker build is development validation only. It is not a production
release artifact. Do not infer that `latest` is in use because its tag changed.
Record and verify the immutable RepoDigest used by each deployed Runner.

## Docker maintenance

For on-host Docker builds, also apply [.agents/skills/production-docker-maintenance/SKILL.md](../production-docker-maintenance/SKILL.md).

Use it during preflight before starting a Docker build, whenever free disk is below its maintenance thresholds, and after a successful deployment to bound accumulated build cache. Do not bypass its volume, rollback-image, or destructive-prune safeguards. A production Docker build must not begin with less than 10 GB of free disk space.

## Rollback image retention

Before replacing a production revision, retain the currently deployed API, Studio,
and Website images under immutable Git revision tags such as
`automaton-prod-api:<git-sha>`. Deploy scripts record the current and immediately
previous revision in blue-green state and accept `--revision` to select an
existing immutable image explicitly for rollback. Never rely on `latest`, dangling
images, or build cache as the rollback artifact.

If blue-green state predates revision metadata, run
`scripts/prod-adopt-image-revisions.sh` once with an explicit known revision for
each live service being adopted, for example `--api <sha> --studio <sha>
--website <sha>`. The command only verifies the live containers, tags their exact
images, and records current revision metadata; it does not build, deploy, restart,
switch colors, or change the database. Stop if any live image cannot be verified.

If a live Studio or Website image cannot be attributed to a known Git revision,
do not guess one. First preserve and verify an immutable recovery tag for the
exact running image, then pass that tag to the corresponding redeploy command:
`--current-artifact-ref <image-ref>`. The script compares the reference's image
ID with the live container before it builds or changes traffic. This one-time
path leaves historical Git revision metadata blank; after the new revision is
accepted, the normal immutable revision metadata becomes authoritative.

For a recovery rollback after the old color has stopped, use the separate
`--recovery-artifact-ref <image-ref>` option. The complete one-time sequence is:

```text
live unknown image
  -> capture its exact image ID
  -> create a dedicated non-latest recovery tag
  -> verify tag and live image IDs match
  -> deploy the new known Git revision
  -> retain the recovery tag through Stage 4 acceptance
```

If rollback is required, deploy the verified recovery tag without a build,
complete the normal health and reachability checks, and then cut traffic back.
The service's current Git revision becomes blank/unknown; the Docker artifact
reference and Git revision are separate kinds of provenance. Never infer a Git
SHA from a Docker tag or use `latest` as the recovery artifact.

## Safety rules

1. Run read-only preflight checks on the production checkout, live colors, services, and protected state.
2. Keep Git network operations under the production `automaton` user. Keep network Git operations separate from privileged filesystem operations. Never copy credentials to root.
3. Inspect tracked changes before advancing production. Never reset, clean, stash, overwrite, or delete production state without explicit approval. Treat private keys, `.env`, `secrets/`, `venv/`, backups, databases, generated runtime files, and test data as protected.
4. Fetch `origin/main` and advance the checkout with a fast-forward only.
5. Select services from the merged diff. Do not rebuild unchanged services.
6. Render Traefik dynamic configuration atomically. Observe and validate the file-provider update before cutover. A Traefik ping alone does not validate the update.
7. Build and warm the inactive color before cutover. Keep the previous color until health, warmup, routing, and rollback checks pass.
8. For API cutovers, after switching traffic the redeploy script calls the active API to publish durable reconnect commands on the existing Runner management queues for Runners still recorded on the previous API color. All connected Runners on that color must support protocol version 5; otherwise restore the prior API route and update those Runners before retrying. This bounded tunnel reconnect is not a management drain or registration-completion gate: do not poll drain status or require Runners to register before deployment completes. The Runner completes active tunnel requests when possible, then reconnects; an in-flight tunnel may be interrupted after its 20-second grace. If forward command publication fails, restore the prior route first, then use the still-running new API image to publish compensation commands targeting the restored color to all registered protocol-v5 Runners, including disconnected Runners. The Runner retains the latest requested color until its API handshake confirms that target. This catches commands already delivered or still queued, and does not call the older API endpoint. If compensation publication fails, keep both API colors running and retry reconciliation. Full-stack rollback restores API, Studio, and Website selectors together before tunnel reconciliation.
9. Run `scripts/prod-verify.sh` and report the deployed revision, services, live colors, tunnel reconnect signal result, and skipped services.

Do not hardcode a service color, public domain, port, or secret. Do not delete production data in a deployment change. Inspect any migration and affected rows first.

For shell changes, run `bash -n` on every changed production script. For production image or Dockerfile changes, validate the actual Docker build context in addition to a local solution build.
