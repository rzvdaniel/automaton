# Runner Download Publishing

This document describes the current state of native runner download publishing and the intended lightweight path forward.

## Current State

Native runner archives are built by [`azure-pipelines-runner.yml`](/C:/Work/automaton/azure-pipelines-runner.yml).

That pipeline currently produces build artifacts for:

- `automaton-runner-win-x64.zip`
- `automaton-runner-win-arm64.zip`
- `automaton-runner-linux-x64.tar.gz`
- `automaton-runner-linux-arm64.tar.gz`
- `automaton-runner-macos-x64.tar.gz`
- `automaton-runner-macos-arm64.tar.gz`

The pipeline publishes them as Azure DevOps build artifacts named `automaton-runner-packages`.

What it does not do in the checked-in repo:

- it does not publish those artifacts to `https://studio.automaton.monster/downloads/runner/latest/`
- it is not connected to `scripts/prod-redeploy-website.sh`
- it is not connected to `scripts/prod-redeploy-all.sh`
- it is not served by [`Program.cs`](/C:/Work/automaton/src/Automaton.Studio.Server/Program.cs) static-file mappings today

## Why The Old Path Was Removed

The repo still had stale references to a `website-cli-assets` compose profile, but:

- `docker-compose.prod.yml` no longer defines that service
- rebuilding cross-platform archives during website deploys is slow
- blue/green website deploys should stay focused on website delivery, not artifact packaging

## Recommended Lightweight Publish Path

Keep runner archive publishing separate from app redeploys.

Preferred shape:

1. `azure-pipelines-runner.yml` builds the native archives.
2. A dedicated publish step or follow-up pipeline downloads those artifacts.
3. That publish job syncs the files to the public downloads location:
   - `/downloads/runner/latest/`
4. The website and Studio UI simply link to that stable location.

## Good Properties Of This Model

- website deploys stay fast
- runner archive publishing can be triggered only when runner code changes
- failed asset publishing does not block API or website cutovers
- native install remains simple for users

## Possible Implementations Later

Any of these are reasonable:

- Azure DevOps release/publish stage that copies artifacts to the downloads host
- a small `publish-runner-downloads` script run manually when needed
- a storage-backed downloads bucket or CDN origin synced from CI artifacts

The important rule is:

- do not rebuild runner archives during normal `prod-redeploy-website.sh` or `prod-redeploy-all.sh` runs
