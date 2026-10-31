# Install Automaton on your server

The server release installs API, Studio, Worker, MySQL, RabbitMQ and Traefik together with Docker Compose. The installer supports **Ubuntu 24.04 or newer on x86_64** with public DNS. Use an Ubuntu release supported by Docker; LTS releases are recommended for servers. When installing Docker, the installer uses Docker's Ubuntu apt repository, which must publish packages for the host's Ubuntu codename. HTTPS uses HTTP validation by default; Cloudflare DNS validation is optional for wildcard certificates. It uses prebuilt images, generates unique credentials and stores configuration outside any Git checkout.

A server release must first be published by the maintainer using the **Publish Automaton Server** GitHub Actions workflow. Run it from the repository's Actions tab on `main`, choose **Run workflow**, and enter a new version such as `0.1.0` (without the `server-v` prefix). It builds and publishes the API, Worker and Studio images, resolves infrastructure image digests, runs an isolated installation smoke test, then creates a GitHub release tagged `server-v0.1.0` with the installer, bundle and checksum. The commands below require that release; source changes alone do not publish an installer release or upgrade any running instance.

## Before starting

Have a domain you can configure, a certificate notification email, sudo access, and at least 10 GiB free disk space. Docker does not need to be preinstalled: the installer adds Docker Engine and Compose when missing, without replacing an existing Docker Engine. Point these DNS records to the server:

- `api.example.com`
- `studio.example.com`
- `apps.example.com`
- `*.apps.example.com`

Use records that reach the server directly for setup. Keep the API hostname directly reachable afterward because Runners use its TLS broker port 5671. Allow inbound TCP 80 and 443 for HTTPS and 5671 for the TLS Runner broker. MySQL, plaintext RabbitMQ and RabbitMQ administration are not exposed on host ports. Your server must have outgoing internet access to download images and request certificates.

The default HTTPS mode issues certificates through HTTP validation, including individual application hostnames when they are deployed. Port 80 must remain publicly reachable for issuance/renewal. For many application hostnames, `--tls-mode cloudflare` enables a wildcard certificate instead: provide a Cloudflare API token with Zone Read and DNS Edit restricted to the domain's zone. The token prompt is hidden. A local evaluation mode without a public domain is not yet supported.

## Install

For the latest stable server release, download the public installer, review it, then run it. It selects the release from GitHub, downloads the bundle, and verifies its checksum:

```bash
curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/install-automaton.sh -o install-automaton.sh
less install-automaton.sh
sudo bash install-automaton.sh
```

To pin a release, add `--version server-vX.Y.Z`. Alternatively, download and verify the bundle yourself from the [public Releases page](https://github.com/rzvdaniel/automaton/releases). The extracted installer uses the version in its bundle, so it needs no `--version` argument:

```bash
VERSION=server-vX.Y.Z
BASE="https://github.com/rzvdaniel/automaton/releases/download/$VERSION"
curl -fLO "$BASE/automaton-server.tar.gz"
curl -fLO "$BASE/automaton-server.tar.gz.sha256"
sha256sum -c automaton-server.tar.gz.sha256
tar -xzf automaton-server.tar.gz
less automaton-server/install.sh
sudo bash automaton-server/install.sh
```

The version above is a placeholder. The installer installs Docker Engine from Docker's official Ubuntu repository if it is missing. A missing Compose plugin is installed automatically, including on a server with an existing Docker Engine. The installer first tries Ubuntu's `docker-compose-v2` package and falls back to Docker's `docker-compose-plugin` package. It does not replace existing Docker installations or remove conflicting packages automatically.

The standalone installer works without a GitHub account. It selects the newest stable `server-vX.Y.Z` release by default and verifies the downloaded bundle's checksum. The updater is included in the installation and is also available as a public release asset if it must be transferred separately.

You can supply `--domain example.com --email owner@example.com` to avoid those two prompts. The optional Cloudflare DNS token prompt is hidden. Do not put secret values in command arguments or terminal transcripts.

The standalone installer downloads the versioned bundle and verifies its SHA-256 checksum; verify an extracted bundle before running its installer as shown above. The installer creates configuration, pulls digest-pinned application images, runs API startup migrations, and starts the stack. It checks the public Studio/API HTTPS route before printing the setup address. It refuses to take over occupied ports or a different existing installation.

For offline preparation with an already downloaded bundle, extract `automaton-server.tar.gz`, verify its checksum, and run `sudo bash automaton-server/install.sh --domain example.com --email owner@example.com`. `--configure-only` prepares configuration and validates Compose without pulling or starting containers; it can still install missing Docker, Compose, and Python packages, so package repository access is needed if they are absent.

## Create your administrator in Studio

Open `https://studio.example.com/install`. Read the setup token locally:

```bash
sudo cat /etc/automaton/secrets/setup-token
```

Paste it into **Server setup token**, select **Check installation**, and create the administrator with your name, email and password. Use at least 12 characters, including uppercase, lowercase, a number and a symbol. Sign in when setup completes.

The setup page and **Admin → Installation** distinguish **verified** live connection checks from **configured** saved settings. Public DNS and inbound ports stay **unverified** in Studio because checks made from inside the server cannot establish external reachability. Check them from another network. The token is read locally on the server and entered into Studio; the page never reveals its stored value.

The setup token stops authorizing access after setup is committed. The initial administrator's email is confirmed through this server-owner setup flow, so SMTP is not required to get started. The durable completion marker prevents setup from reopening if accounts are later removed. Existing installations with a system administrator remain installed even without this marker.

Under **Admin → Installation**, a system administrator can check database and broker connectivity and see whether public URLs, signing credentials, persistent keys and optional SMTP configuration are present. Configured means presence was checked; it is not proof of delivery, durable storage or a working backup.

Install a **Machine Management Runner** through the existing Studio Runner installation flow when you are ready to deploy applications. You may choose the same host or another machine. This installer does not enroll a Runner automatically or assign apps. Follow [Self-hosted Runners](self-hosted-runners.md) for that separate trust/enrollment step.

## Configuration and secrets

The defaults are `/opt/automaton-server` for the release manifest and Compose file, and `/etc/automaton` for private configuration. Override both paths on every invocation if you use custom paths. Secret configuration files are owner-readable only and application settings are mounted read-only into API and Worker. MySQL consumes password files; RabbitMQ receives its private bootstrap configuration; Traefik receives the DNS token file. The applications never receive MySQL's root password or the DNS token. Studio receives a separate `studio-runtime-config.js` file containing only the public API and app URLs. That file is publicly readable and must never contain credentials. The same Studio image works across domains without rebuilding.

| Setting | Source | Who creates it |
|---|---|---|
| Domain, certificate email | Installer prompts | Operator |
| Cloudflare DNS API token (Cloudflare mode only) | Hidden prompt → `secrets/cloudflare-token` | Operator/provider |
| Database root/app passwords, broker password | `secrets/` and generated service settings | Installer |
| Setup token, RSA signing keys, HMAC key, AI encryption key, runtime proxy key | `secrets/` and `appsettings.Production.json` | Installer, once |
| API/Studio/app URLs and broker TLS address | Domain → application/Traefik settings | Installer |
| Data Protection keys | API and Worker key volumes | Services; preserve across updates |
| SMTP, GitHub OAuth, Stripe | `appsettings.Production.json` | Operator, optional |
| Runner credentials | Studio enrollment flow | Automaton, per Runner |

Public signup, GitHub OAuth, billing credentials and legacy Hosted Runner provisioning start unconfigured/disabled. After creating the first administrator, **Admin → Installation → Outgoing email** can save SMTP host, port, TLS mode, account, password, sender address and support address. Studio stores the settings in the database with the SMTP document encrypted by the API Data Protection key ring. Its password is never returned to Studio. A successful **Check connection** verifies TLS and authentication, not inbox delivery; saving changes clears that verification. **Send test email** sends only to the recipient explicitly entered by the administrator; SMTP acceptance is not proof of inbox delivery. Database settings take effect for subsequent messages without a restart and override the file-based `EmailConfig`. If they cannot be decrypted, sending fails and Studio reports the key-ring problem rather than using the file configuration.

The protected `appsettings.Production.json` remains the fallback until SMTP is saved through Studio. For GitHub, configure `GitHub.ClientId`/`ClientSecret`; the generated API/frontend URLs are already set. OAuth and billing credentials are not editable in the installation page. After editing those file settings securely on the host, recreate API and Worker to apply changes:

```bash
cd /opt/automaton-server
sudo docker compose --env-file release.env --env-file .env -f compose.yaml up -d --force-recreate api worker
```

Preserve generated encryption/signing keys and Data Protection volumes. Back up the API Data Protection key ring together with the database; restored Studio-managed SMTP settings cannot be decrypted without the matching keys. Replacing keys can invalidate sessions or make existing encrypted records unreadable. Re-running the same installer preserves credentials and operator edits; missing keys fail with a restore instruction instead of silently replacing them. A different domain, TLS mode, configuration directory or release manifest is refused. This command resumes first installation; use the updater below for a new release. The retired source deployment scripts have been removed. Existing servers require a reviewed adoption before using the installer-managed updater.

## Update an installed server

Run the updater that the installer placed beside the Compose file. Without a version, it selects the newest stable server distribution available publicly on GHCR:

```bash
sudo bash /opt/automaton-server/update.sh
```

To select a particular published release, add `--version server-vX.Y.Z`. The updater refuses a version older than the installed version. Offline `--bundle` updates still require an explicit `--version`.

The same updater has a separate mode for a marketing website retained in an existing Compose project. Supply the immutable website image reference from the successful `Build website` workflow artifact, the retained Compose file and service, and the public URL:

```bash
sudo bash /opt/automaton-server/update.sh \
  --website-image ghcr.io/OWNER/REPO-website@sha256:DIGEST \
  --website-compose /path/to/docker-compose.prod.yml \
  --website-service website-green \
  --website-url https://YOUR_DOMAIN
```

This mode pulls the specified image, tags the prior container image with a `-previous` suffix, updates only the selected website service with `--no-deps --no-build`, waits for its health check, and checks the public URL. It does not update the server release or run database backup and migration steps. Use it only for a retained website service whose Compose image is a tag and whose current container is running.

For installations before the updater was bundled, transfer the reviewed `update-automaton.sh` release asset once and run it as shown above, with or without an explicit version. The updater pulls the release bundle from the public `ghcr.io/rzvdaniel/automaton-server-distribution` package, verifies the bundle checksum and version, checks the current stack, and refuses releases that change Compose or infrastructure image digests. It makes a root-only recovery directory under `/opt/automaton-server/backups` (override with `--backup-dir`), containing a consistent MySQL dump, configuration, API data, API and Worker Data Protection keys, and the previous release manifest. It verifies the archives and checksums before pulling and applying the new API, Worker and Studio images. Compose recreates only services whose image changed. Public Studio/API routes are checked before the installed version is recorded. Existing domains, credentials, database and volumes are preserved.

Copy the recovery directory to encrypted off-host storage, and retain the matching database and key-ring backup. A local backup alone does not protect against host loss. If an update fails after containers begin changing, do not rerun it blindly: the previous manifest and recovery data are retained, but database migrations may make an image-only rollback unsafe. Restore the database, configuration and keys together before bringing back old images when a migration has run. The updater intentionally does not perform an automatic database rollback that could discard writes after cutover. Use `--bundle /path/automaton-server.tar.gz --checksum /path/automaton-server.tar.gz.sha256` for an authenticated or offline release transfer.

The distribution package and GitHub release assets are publicly readable, so installation and updates need no GitHub account. Legacy servers require a reviewed adoption before using this updater.

### Adopted production installations

Registered app routes alone claim published app hostnames; wildcard TLS certificates do not route unknown or deleted hosts to the platform API.

A reviewed migration can adopt an existing production server into this layout. The fresh installer does not perform that migration. Preserve the original database and broker versions, credentials, broker node identity, application and Data Protection volumes, certificate store, networks and public routes before cutting over. Never attach an existing database volume to a newer database image as part of layout adoption.

An adopted installation may keep a root-owned `compose.override.yaml` beside `compose.yaml`. The updater applies this same override to the installed and candidate release, backs it up with checksums, and preserves it across updates. This allows external existing volumes/networks and exact infrastructure image digests to remain pinned while API, Worker and Studio follow normal releases. Infrastructure pins in the override take precedence over the release manifest and require their own reviewed upgrade. Keep application image references driven by the release manifest so application updates still take effect. Review the effective configuration locally with `docker compose --env-file release.env --env-file .env -f compose.yaml -f compose.override.yaml config`; its output can contain secrets and must not be shared.

Use `update.sh` for subsequent releases. `install.sh --status` supports the override; rerunning the fresh installer against an override installation is refused to protect its imported settings. Manual Compose commands on an adopted server must also include `-f compose.override.yaml`. Retain the stopped original containers and verified configuration/data backups until public routes, database records, broker connectivity and an actual subsequent updater run have been checked. The public marketing website is outside the standalone server bundle and needs its own retained service and route when migrating a server that hosts it.

## Back up before relying on the installation

Ignored files and filesystem permissions do not prevent accidental deletion. Back up `/etc/automaton`, `/opt/automaton-server`, the MySQL volume and the API data/API key/Worker key volumes to encrypted off-host storage. Keep the backup decryption key separately. Use an application-consistent database backup or quiesced volume snapshot and verify a restore on another host. Certificate and broker volumes also contain persistent operational state. No remote backup destination or automatic backup scheduler is configured by this installer.

## Status and recovery

```bash
sudo bash /opt/automaton-server/install.sh --status
cd /opt/automaton-server
sudo docker compose --env-file release.env --env-file .env -f compose.yaml logs --tail 100 api worker traefik
```

Logs can contain operational details; redact them before sharing. If DNS/TLS verification fails, correct the records or token and rerun the same release/domain/email. Already generated credentials and volumes are preserved. Never use `docker compose down -v` to retry installation: it deletes persistent data. If secrets are lost, restore the configuration and persistent keys together rather than generating replacements against an existing database.

## Publishing a server release (maintainers)

Run `.github/workflows/server-release.yml` on the intended reviewed revision with a version such as `0.1.0`. It builds/pushes API, Worker and a domain-independent Studio image to GHCR, preserves the prior release's infrastructure image digests, tests an isolated first-administrator claim against MySQL/RabbitMQ, and publishes the bundle/checksum/installer/updater under `server-v0.1.0`. It also publishes a distribution image containing the bundle and checksum for anonymous updates. All four GHCR packages must be publicly readable; the workflow verifies anonymous pulls before publishing the release. On first publication of the distribution package, a repository owner may need to change its visibility to Public, then rerun the workflow. Runner images are built and published separately by `.github/workflows/runner-image.yml` to GHCR; existing configured image references and release manifests are preserved until explicitly updated.

The local configuration tests and the isolated Docker smoke test are development validation. Public DNS/certificates and a clean Ubuntu/Docker installation require acceptance on a disposable server before declaring a release generally supported.
