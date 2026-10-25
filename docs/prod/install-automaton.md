# Install Automaton on your server

The server release installs API, Studio, Worker, MySQL, RabbitMQ and Traefik together with Docker Compose. The installer supports **Ubuntu 24.04 or newer on x86_64** with public DNS. Use an Ubuntu release supported by Docker; LTS releases are recommended for servers. When installing Docker, the installer uses Docker's Ubuntu apt repository, which must publish packages for the host's Ubuntu codename. HTTPS uses HTTP validation by default; Cloudflare DNS validation is optional for wildcard certificates. It uses prebuilt images, generates unique credentials and stores configuration outside any Git checkout.

A server release must first be published by the maintainer using the **Publish Automaton Server** GitHub Actions workflow. Run it from the repository's Actions tab on `main`, choose **Run workflow**, and enter a new version such as `0.1.0` (without the `server-v` prefix). It builds and publishes the API, Worker and Studio images, resolves infrastructure image digests, runs an isolated installation smoke test, then creates a GitHub release tagged `server-v0.1.0` with the installer, bundle and checksum. The commands below require that release; source changes alone do not publish an installer release or upgrade any running instance.

## Before starting

Have a domain you can configure, a certificate notification email, sudo access, and at least 10 GiB free disk space. A missing Docker Compose plugin is installed automatically from Ubuntu's repository when available, or from Docker's Ubuntu repository otherwise, without replacing an existing Docker Engine. Point these DNS records to the server:

- `api.example.com`
- `studio.example.com`
- `apps.example.com`
- `*.apps.example.com`

Use records that reach the server directly for setup. Keep the API hostname directly reachable afterward because Runners use its TLS broker port 5671. Allow inbound TCP 80 and 443 for HTTPS and 5671 for the TLS Runner broker. MySQL, plaintext RabbitMQ and RabbitMQ administration are not exposed on host ports. Your server must have outgoing internet access to download images and request certificates.

The default HTTPS mode issues certificates through HTTP validation, including individual application hostnames when they are deployed. Port 80 must remain publicly reachable for issuance/renewal. For many application hostnames, `--tls-mode cloudflare` enables a wildcard certificate instead: provide a Cloudflare API token with Zone Read and DNS Edit restricted to the domain's zone. The token prompt is hidden. A local evaluation mode without a public domain is not yet supported.

## Install

Choose a published release version from the repository's Releases page, download its installer, inspect it, and run it:

```bash
VERSION=server-vX.Y.Z
curl -fL "https://github.com/rzvdaniel/automaton/releases/download/$VERSION/install-automaton.sh" -o install-automaton.sh
less install-automaton.sh
sudo bash install-automaton.sh --version "$VERSION" --install-docker
```

The version above is a placeholder. `--install-docker` authorizes installing Docker Engine from Docker's official Ubuntu repository if Docker is missing. A missing Compose plugin is installed automatically, including on a server with an existing Docker Engine. The installer first tries Ubuntu's `docker-compose-v2` package and falls back to Docker's `docker-compose-plugin` package. It does not replace existing Docker installations or remove conflicting packages automatically.

You can supply `--domain example.com --email owner@example.com` to avoid those two prompts. The optional Cloudflare DNS token prompt is hidden. Do not put secret values in command arguments or terminal transcripts.

The installer downloads the versioned bundle, verifies its SHA-256 checksum, creates configuration, pulls digest-pinned application images, runs API startup migrations, and starts the stack. It checks the public Studio/API HTTPS route before printing the setup address. It refuses to take over occupied ports or a different existing installation.

For offline preparation with an already downloaded bundle, extract `automaton-server.tar.gz`, verify its checksum, and run `sudo bash automaton-server/install.sh --domain example.com --email owner@example.com`. `--configure-only` prepares configuration and validates Compose without pulling or starting containers; Docker and Python prerequisites must still be available.

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

Public signup, GitHub OAuth, billing credentials and legacy Hosted Runner provisioning start unconfigured/disabled. Add external integrations only when needed. SMTP settings use the existing `EmailConfig` fields: `EmailHost`, `EmailPort`, `EmailUseSsl`, `EmailAccount`, `EmailPassword`, `FromEmail` and `SupportEmail`. For GitHub, configure `GitHub.ClientId`/`ClientSecret`; the generated API/frontend URLs are already set. Integration credentials are not editable in the new status page. After editing settings securely on the host, recreate API and Worker to apply changes:

```bash
cd /opt/automaton-server
sudo docker compose --env-file release.env --env-file .env -f compose.yaml up -d --force-recreate api worker
```

Preserve generated encryption/signing keys and Data Protection volumes. Replacing them can invalidate sessions or make existing encrypted records unreadable. Re-running the same installer preserves credentials and operator edits; missing keys fail with a restore instruction instead of silently replacing them. A different domain, TLS mode, configuration directory or release manifest is refused. This command resumes first installation; it is not an upgrade tool. Existing blue/green production deployments continue using their existing scripts and are not migrated by this installer.

## Back up before relying on the installation

Ignored files and filesystem permissions do not prevent accidental deletion. Back up `/etc/automaton`, `/opt/automaton-server`, the MySQL volume and the API data/API key/Worker key volumes to encrypted off-host storage. Keep the backup decryption key separately. Use an application-consistent database backup or quiesced volume snapshot and verify a restore on another host. Certificate and broker volumes also contain persistent operational state. No remote backup destination or automatic backup scheduler is configured by this installer.

## Status and recovery

```bash
sudo bash install-automaton.sh --status
cd /opt/automaton-server
sudo docker compose --env-file release.env --env-file .env -f compose.yaml logs --tail 100 api worker traefik
```

Logs can contain operational details; redact them before sharing. If DNS/TLS verification fails, correct the records or token and rerun the same release/domain/email. Already generated credentials and volumes are preserved. Never use `docker compose down -v` to retry installation: it deletes persistent data. If secrets are lost, restore the configuration and persistent keys together rather than generating replacements against an existing database.

## Publishing a server release (maintainers)

Run `.github/workflows/server-release.yml` on the intended reviewed revision with a version such as `0.1.0`. It builds/pushes API, Worker and a domain-independent Studio image to GHCR, resolves image digests, tests an isolated first-administrator claim against MySQL/RabbitMQ, and publishes the bundle/checksum/installer under `server-v0.1.0`. The three GHCR packages must be publicly readable; the workflow verifies anonymous pulls before publishing the release. On first publication, a repository owner may need to change package visibility to Public, then rerun the workflow. Runner images remain under their existing Azure DevOps release authority.

The local configuration tests and the isolated Docker smoke test are development validation. Public DNS/certificates and a clean Ubuntu/Docker installation require acceptance on a disposable server before declaring a release generally supported.
