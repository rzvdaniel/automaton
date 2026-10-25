---
name: production-installation-setup
description: Bootstrap or audit a fresh Automaton production installation, including its required configuration, secrets, validation and backup.
license: Apache-2.0
---

# Production installation setup

Use this skill when preparing or reviewing a fresh Automaton production installation or documenting the settings an operator must provide. For deployment and cutover operations, also follow [production-deployment](../production-deployment/SKILL.md).

## Fresh server installer

For a new standalone server, use [the public installation guide](../../../docs/prod/install-automaton.md), `scripts/install-automaton.sh`, and the release bundle under `docker/install/`. The initial supported target is Ubuntu 24.04 x86_64 with public DNS. The release workflow publishes prebuilt, digest-pinned server images and a checksum-verified bundle. A fresh install stores protected settings outside the checkout in `/etc/automaton`; Studio `/install` requires its generated setup token. Re-running preserves credentials. This is separate from the existing production blue/green workflow and does not migrate it.

## Find the authoritative settings

Do not use a live server's untracked settings file as the setup template. Read the tracked examples and current consumers:

- `.env.prod.example` and `docker-compose.prod.yml` for production services, domains, DNS/TLS, MySQL, RabbitMQ, runtime proxy authentication, image/network settings and retention.
- `src/Automaton.Api/appsettings.Production.example.json` for shared API/Worker application settings and optional integrations.
- `.env.runner.example`, `docker-compose.runner.yml` and `docs/prod/deploying-runner.md` for independently installed Runners.
- `docs/prod/production-docker-cheat-sheet.md` and `docs/prod/self-hosted-runners.md` for host prerequisites, persistent data and recovery notes.

For each setting, identify its consumer and classify it as operator-provided, generated per installation, derived from another setting, or optional. Confirm required versus optional by checking Compose interpolation and application startup validation; do not infer that every example field is mandatory. Keep the checklist synchronized with the example files and consumers.

The current templates cover these groups: `.env.prod.example` holds public domains, ACME/Cloudflare DNS, runtime-proxy authentication, MySQL and RabbitMQ credentials, the private infrastructure broker address, deployment timing and binlog retention. The production appsettings example covers database/broker connection details, JWT/RSA/HMAC and application encryption keys, data-protection paths, public URLs/CORS, Runner deployment/SSH settings, and optional GitHub OAuth, Turnstile, SMTP and Stripe integrations. `.env.runner.example` separately requires the Automaton server URL and Runner API key, with an optional Runner name. Generate the exact per-install checklist from the current templates and code; do not copy example values as production credentials.

## Protect and generate configuration

- Never print, log, commit, or include live secret values in a report. Report key names and states such as missing, present, or validated; mask any incidental value output.
- Generate unique high-entropy passwords and keys for each installation. Do not copy this server's credentials, use example defaults, or rotate encryption/signing/data-protection keys casually; rotation can invalidate tokens, signatures, or encrypted records.
- Keep live settings out of Git. The current production checkout mounts `/opt/automaton/config/appsettings.Production.json` read-only into API and Worker. Do not move or rewrite it during an audit. A future path migration must update the Compose mount and deployment documentation together.
- Write new settings atomically with restrictive permissions (`umask 077`, owner-readable only). Preserve existing configuration before updates and verify the backup before replacing the live file.
- An ignored or untracked file is not a backup. Keep an encrypted, off-host backup of installation configuration and persistent key material; store the decryption key separately and test restoration. Do not rely on the same server, checkout, or Docker volume as the only copy.
- Check `git status` and `git check-ignore -v` for runtime configuration and backup suffixes. Ignore rules prevent accidental staging but do not protect files from deletion.

## Validate without exposing secrets

Provide an operator checklist that distinguishes file presence from working service checks. Validate required file structure, permissions, domain/DNS/TLS, database and broker connectivity, data-protection persistence, and any enabled external integration. Do not echo credentials in command output or health diagnostics.

For a user-facing setup flow, prefer a guided installer that generates per-install secrets, writes protected files, validates dependencies, and reports actionable errors. A status page may show configured/missing/healthy states and masked identifiers, but must never return secret values. Keep initial bootstrap local or otherwise access-controlled; do not create a public endpoint that reveals configuration.
