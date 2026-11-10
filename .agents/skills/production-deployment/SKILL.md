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
3a. If the release changes the installer, the CLI login/deploy/update/backup commands or the install, first-admin or deploy docs, run the new-user check (section below) on the test server before updating production. Skip it for releases that touch none of those.
4. Run the installation's `update.sh` (default `/opt/automaton-server/update.sh`). It verifies the release checksum, backs up configuration/database/key material and updates the application images. Never use the fresh installer to overwrite an existing configuration.
5. Adopted installations can retain a reviewed `compose.override.yaml`; the updater must apply and back up the same override for the installed and candidate release. Do not silently upgrade their pinned database/broker versions.
6. Verify container state, origin and public API/Studio/app routes, installation status and the certificate-validated Runner broker connection. Verify requested application behavior separately from deployment health.
7. Close out: run `python .agents/tools/close-released-issues.py` (dry run, prints one line per `verified`/`verified-partial` issue; add `--website-sha <sha>` when the website was deployed, `--published N,M` for apps the owner published). Check the table, then re-run with `--apply`: it checks the close gate's release part (merge commit contained in the production version), adds `live`, closes with `Live on production in <version>` and sets board Status Done. Exit code 1 means some issue "needs a human": handle it by hand. A `verified-partial` issue closes only with `--accept N --reason "<owner's words>"`. Leave the others open and list them with the missing gate item.
8. Report the installed release, updated services, verification results and any unavailable check. The marketing website remains separate from the standalone server bundle.

The updater's protected recovery directory is the rollback record. Image rollback after a migration may be unsafe; assess the database before restoring an earlier manifest. Retain backups unless the user explicitly authorizes their removal. Never delete live volumes as deployment cleanup.

## New-user check

Why: tests run by agents that know the repo hide the gaps a newcomer hits (unclear docs, a login that only works one way). A fresh session that sees only the public website finds them.

How: start a headless task on the test server with the second account (Sonnet 5.5, low effort) from an EMPTY folder, with no repo, AGENTS.md or project instructions, on a freshly rebuilt server (rebuild only when the installer changed). Prompt, kept this short on purpose:

```
I have a Debian server: `ssh <server>`, domain <domain>, email <email> for certificate notices.
Install Automaton on it (https://automaton.monster) and deploy a small web app of your choice to it.
Tell me the app's address when done, and write ./report.md about what was easy and where you got stuck.
```

Each from-scratch install uses a fresh base name (nu5, nu6, ...) because Let's Encrypt allows only 5 certificates per week for the same set of names.

Result: read `report.md`, open an issue for every gap (write-issue skill), fix them, then update production. A run that cannot finish blocks the production update. Run it also once per major launch step (for example a new supported OS). Do not add checklists or timing tables to this prompt; that is the separate measured pass of #561.

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
