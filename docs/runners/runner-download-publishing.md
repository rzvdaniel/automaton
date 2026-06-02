# Runner Download Publishing

This document describes the current state of native runner download publishing and the intended lightweight path forward.

Status note:

- this is no longer a launch-critical path
- the default runner install path is Docker
- advanced native installs can also use the NuGet tool package for `Automaton.Runner`
- archive hosting is only needed if you want CLI-managed native archive installs

## Current State

Native runner archives are not the primary distribution path anymore.

The runner is now expected to be publishable as a NuGet tool package from [`azure-pipelines-runner.yml`](/C:/Work/automaton/azure-pipelines-runner.yml), while Docker remains the default runner distribution path.

If you still want archive-based native installs later, the archive layout would need to produce:

- `automaton-runner-win-x64.zip`
- `automaton-runner-win-arm64.zip`
- `automaton-runner-linux-x64.tar.gz`
- `automaton-runner-linux-arm64.tar.gz`
- `automaton-runner-macos-x64.tar.gz`
- `automaton-runner-macos-arm64.tar.gz`

The old archive-hosting approach is now optional rather than required.

What is no longer required for launch:

- public archive hosting under `https://studio.automaton.monster/downloads/runner/latest/`
- coupling native runner distribution to `prod-redeploy-website.sh`
- coupling native runner distribution to `prod-redeploy-all.sh`

## Why The Old Path Was Removed

The repo still had stale references to a `website-cli-assets` compose profile, but:

- `docker-compose.prod.yml` no longer defines that service
- rebuilding cross-platform archives during website deploys is slow
- blue/green website deploys should stay focused on website delivery, not artifact packaging

## Optional Archive Publish Path

If archive-based native installs are revived later, keep archive publishing separate from app redeploys.

Preferred shape:

1. `azure-pipelines-runner.yml` builds the native archives.
2. A dedicated publish step or follow-up pipeline downloads those artifacts.
3. That publish job uploads the files to the Hetzner static downloads path:
   - `/opt/automaton-downloads/runner/releases/<version>/`
   - `/opt/automaton-downloads/runner/latest/`
4. Hetzner nginx serves those files at:
   - `/downloads/runner/latest/`
   - `/downloads/runner/releases/<version>/`
5. The website and Studio UI simply link to that stable location.

## Hetzner Folder Contract

Create these folders once on the host:

```bash
sudo mkdir -p /opt/automaton-downloads/runner/latest
sudo mkdir -p /opt/automaton-downloads/runner/releases
sudo chown -R runner-deploy:runner-deploy /opt/automaton-downloads
```

Recommended contents:

- `/opt/automaton-downloads/runner/latest/automaton-runner-win-x64.zip`
- `/opt/automaton-downloads/runner/latest/automaton-runner-win-arm64.zip`
- `/opt/automaton-downloads/runner/latest/automaton-runner-linux-x64.tar.gz`
- `/opt/automaton-downloads/runner/latest/automaton-runner-linux-arm64.tar.gz`
- `/opt/automaton-downloads/runner/latest/automaton-runner-macos-x64.tar.gz`
- `/opt/automaton-downloads/runner/latest/automaton-runner-macos-arm64.tar.gz`
- `/opt/automaton-downloads/runner/latest/version.json`
- `/opt/automaton-downloads/runner/latest/checksums.txt`

Mirror the same file set under each versioned release directory.

## Nginx Static Mapping

Serve runner downloads from the top-level host nginx, not from the API, Studio, or website containers.

Conceptual nginx block:

```nginx
location /downloads/runner/ {
    alias /opt/automaton-downloads/runner/;
    autoindex off;
    add_header Cache-Control "public, max-age=300";
    add_header X-Content-Type-Options "nosniff";
    try_files $uri =404;
}
```

This should make URLs like these work directly:

- `https://studio.automaton.monster/downloads/runner/latest/automaton-runner-linux-x64.tar.gz`
- `https://studio.automaton.monster/downloads/runner/releases/1.0.12/automaton-runner-linux-x64.tar.gz`

If installer scripts such as `install.sh` and `setup-service.sh` are also served from the same hostname, keep them on the same nginx host and treat them as separate static files under `/downloads/runner/`.

## Native Distribution Direction

[`azure-pipelines-runner.yml`](/C:/Work/automaton/azure-pipelines-runner.yml) now targets NuGet publishing for the native runner tool package instead of SSH upload for archive hosting.

That means:

- Docker Hub remains the default runner distribution path
- NuGet becomes the advanced native distribution path
- archive hosting can stay deferred unless CLI-managed native archives become important again

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
