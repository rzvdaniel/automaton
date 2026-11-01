---
name: production-deployment
description: Operate installer-managed Automaton releases, Runner publishing, routing, verification and recovery.
license: Apache-2.0
---

# Production deployment

A live deployment requires an explicit user request. Use the versioned installer and updater described in [the installation guide](../../../docs/prod/install-automaton.md). Source blue/green deploy scripts have been retired.

## Server release and update

1. Check the Git working tree and preserve unrelated changes. Keep Git network operations under the deployment user; never copy Git credentials to root.
2. Validate affected code and scripts, merge the reviewed change, and publish a versioned bundle through `.github/workflows/server-release.yml`. Its `cli` input (default `auto`) also publishes the CLI to NuGet when `src/Automaton.Cli` or `src/Automaton.Core` changed since the previous release, so users never get a CLI older than the server's `automaton.json` contract; check that the started **Pack CLI and Runner** run succeeds.
3. Inspect migrations and existing data before applying a release. Preserve configured signing/encryption keys, credentials, Data Protection keys and persistent volumes.
4. Run the installation's `update.sh` (default `/opt/automaton-server/update.sh`). It verifies the release checksum, backs up configuration/database/key material and updates the application images. Never use the fresh installer to overwrite an existing configuration.
5. Adopted installations can retain a reviewed `compose.override.yaml`; the updater must apply and back up the same override for the installed and candidate release. Do not silently upgrade their pinned database/broker versions.
6. Verify container state, origin and public API/Studio/app routes, installation status and the certificate-validated Runner broker connection. Verify requested application behavior separately from deployment health.
7. Report the installed release, updated services, verification results and any unavailable check. The marketing website remains separate from the standalone server bundle.

The updater's protected recovery directory is the rollback record. Image rollback after a migration may be unsafe; assess the database before restoring an earlier manifest. Retain backups unless the user explicitly authorizes their removal. Never delete live volumes as deployment cleanup.

## Runner image release authority

Production Runner Docker images are built and published by the repository's
GitHub Actions workflow, `.github/workflows/runner-image.yml`, publishing to GHCR.

Do not create a parallel production Runner build or publishing path on a
production host or developer machine. For changes under `src/Automaton.Runner/`:

1. Validate the change locally as appropriate.
2. Merge it to the intended release revision.
3. Let the GitHub Actions Runner image workflow build and publish the production image.
4. Verify the exact published image digest and image-level smoke checks.
5. Deploy or install Runners from that CI-produced artifact.

A local Docker build is development validation only. It is not a production
release artifact. Do not infer that `latest` is in use because its tag changed.
Record and verify the immutable RepoDigest used by each deployed Runner.

## Storage and routing

Apply [production Docker maintenance](../production-docker-maintenance/SKILL.md) for disk preflight and cleanup. Preserve active images, configured Runner artifacts and retained rollback images. No blanket image or volume pruning.

Apply routing configuration atomically. Verify actual routing after the file-provider update; a Traefik ping is insufficient. Only registered app routes should claim published app hostnames; wildcard TLS certificates do not require a wildcard API fallback. Deleted or unknown app hostnames must return 404.

For shell changes, run `bash -n` on every changed script. Validate production image changes with their actual Docker build context and CI artifact. Never log secrets or raw configuration values.
