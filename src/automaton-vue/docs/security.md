---
title: Security overview
description: How an Automaton installation isolates apps, protects secrets and app permissions, audits changes, and what is out of scope.
order: 160
---

# Security overview

How an Automaton installation protects apps, secrets and administrator control today. This page describes current behaviour, not plans. To report a problem, see [SECURITY.md](https://github.com/rzvdaniel/automaton/blob/main/SECURITY.md).

## Who runs what

- **Your server** runs the API, Worker, Studio and app store in containers behind Traefik with HTTPS. You install and update it; the Automaton project hosts nothing for you.
- **Self sign-up is disabled.** Accounts are created or invited by an administrator.
- **Runners** are trusted management services, one per machine. A Runner connects to the server's message broker over TLS (port 5671) with its own credential and receives work only for the deployment targets assigned to it.

## How apps are isolated

User code does not run inside the Runner process. APIs, server-rendered sites, Actions and builds run in separate Docker containers that the Runner creates with:

- all Linux capabilities dropped and `no-new-privileges`;
- the CPU, memory and process limits the server configures;
- the deployed app files mounted read-only, plus a per-app data directory for persistent data;
- for Actions and builds, a non-root user; Actions also get a read-only root filesystem.

Generic deployments do not accept host mounts, privileged containers, host networking or the Docker socket. Static sites share one Nginx serving container that serves each app's published files.

Containers are a process boundary, not a virtual machine. On a shared machine they suit people who trust each other (one person, a team, an organisation), not mutually untrusted tenants. Put workloads you do not trust on a separate machine with its own Runner.

## Runners and the docker socket

A Machine Management Runner has access to the host's Docker socket to manage bounded app and Action containers, so a compromised Runner could control host Docker. Install it only on machines you trust, keep user code in the isolated containers above, and keep a Runner's enrollment command private: it is a one-time secret, returned once and stored by the API as a hash.

## Secrets

- App, project and personal secrets are stored encrypted on the server (ASP.NET Core Data Protection).
- Values are write-only: Studio, the API and the CLI never return a value after it is saved. `automaton secrets set` reads the value from standard input so it stays out of shell history and AI chat.
- Secrets reach an app's Actions and runtime containers as environment variables. A Runner fetches them with its own credential, only for targets assigned to it. Secret values are redacted from Action logs and results.
- Static sites cannot use secrets, because everything they ship reaches the browser.

## App permissions

An app that needs platform access declares permissions in `automaton.json` (for example `backups:read` the read-only `server:read` and `security:read` (fail2ban bans, no firewall access), `security:manage` (unban or allowlist an address for a system administrator; approve only for an app that administrators alone can open), `server:cleanup` (read the nightly cleanup and change or run it; approve only for an app that administrators alone can open), or `notify:email`, which only emails the server's own users and an administrator-managed allowed list). Nothing is granted until a system administrator approves the request in Studio; the platform then issues a scoped key and stores it as an app secret. The API rejects calls outside the approved permissions, and revoking the approval stops the next call.

## Audit log

Secret changes and uses (names, scopes and versions, never values) are recorded in the platform audit log and kept for 180 days; app permission requests, approvals and revocations are recorded there too. App owners see their app's history under **Secrets → Show history**; administrators see all events under **Admin → Audit Events**.

## SSH and the firewall

At the end of an install the installer checks how the VPS is set up: SSH password login, root login with a password, ports other than 22, 80, 443 and 5671 (Runners) open to the internet, and whether a firewall is on. It prints what it found and, when you run it in a terminal, asks before each fix, for example "SSH allows passwords. Turn it off? [Y/n]". Turning passwords off is written to `/etc/ssh/sshd_config.d/00-automaton-hardening.conf` (delete that file to allow passwords again) and is refused when no account has an authorized SSH key, so you cannot lock yourself out. The firewall fix turns on `ufw` with those ports and the port sshd listens on. With `--unattended` or `--quiet` the installer only warns and changes nothing. Run the check again any time: `sudo python3 /opt/automaton-server/host_security.py`.

Server Keeper shows the same findings as a "needs you" item once the Runner has measured them (a few minutes). It cannot change them. Cloud provider firewalls are not checked, and Server Keeper cannot see `iptables` or `nftables` rules, so it does not report a missing firewall; the installer does.

## Security headers

Studio, the API, the store and the apps address send `Strict-Transport-Security` (one year, subdomains included) and `X-Content-Type-Options: nosniff`. Studio, the API and the store also forbid being framed and send a Content-Security-Policy; the apps address allows framing only by Studio, which shows apps in a frame. nginx does not reveal its version. The installer and updates add the headers to installations it configured; a hand-edited `dynamic/installation.yml` is left as it is, so add the `security-headers@file` middleware to its routers yourself. Apps' own responses keep the headers the apps set.

## Scanners and password guessing

The installer and every update set up [fail2ban](https://github.com/fail2ban/fail2ban) with three jails: `sshd`, `automaton-scanner` (20 requests for junk paths such as `/.env`, `/wp-admin` or `*credentials*` that answer 404, or 404 bursts on Studio, the API root and the store, within a minute) and `automaton-signin` (10 failed Automaton sign-ins or rejected API keys within ten minutes). A banned address is blocked at the firewall (Docker's `DOCKER-USER` chain, so ports 80, 443 and 5671 are really closed to it) for an hour, longer each time it returns. Real traffic is never limited: pages, published apps' links, Studio's live connection and uploads do not count. The jails read Traefik's access log (it keeps no request headers), kept in the certificates volume and rotated daily.

The API also limits attempts per client address (the address Traefik forwards), and answers `429 Too Many Requests` past the limit. Defaults: sign-in 5 a minute; password reset request 5 an hour; password reset 5 an hour; opening, accepting or completing an invitation 10 per ten minutes; approving a CLI or AI sign-in 10 per ten minutes; rejected API keys 10 a minute (after that a request carrying an API key gets 429 until the minute is over, except one with a key the API accepted in the last few minutes, so a Runner on the same address keeps working). Change them in `appsettings.Production.json` under `RateLimiting`, for example `"AuthForgotPassword": { "PermitLimit": 5, "WindowSeconds": 3600 }` (names: `AuthLogin`, `AuthForgotPassword`, `AuthResetPassword`, `InvitationAccept`, `CliApproval`, `ApiKeyFailures`). Normal signed-in traffic is not affected. A rejected API key carries an `X-Automaton-Auth-Failure` response header, which Traefik's access log keeps so `automaton-signin` can ban the address.

To never ban an address, for example an office behind one shared IP, create `/etc/fail2ban/jail.local` if it does not exist and add a `[DEFAULT]` section with your address, keeping the loopback ones: `[DEFAULT]` then `ignoreip = 127.0.0.1/8 ::1 203.0.113.7` (space-separated, one line; `sshd` and the Automaton jails extend it, so the reload is enough). Run `sudo fail2ban-client reload`; `sudo fail2ban-client status` lists all jails. The installer and updates also add the address you run them from in an SSH terminal session to the `sshd` jail's allowlist, so failed logins before the install (a wrong key) cannot lock you out; this works only when the session has a terminal and a real IPv4 or IPv6 address, otherwise allowlist your address yourself. Those addresses stay until you remove them from the `# admin-ssh-ip:` line in `/etc/fail2ban/jail.d/automaton.conf` (then run `fail2ban.sh` again). List bans with `sudo fail2ban-client status automaton-scanner` (or `sshd`, `automaton-signin`) and lift one with `sudo fail2ban-client unban <address>` (for SSH: `sudo fail2ban-client set sshd unbanip <address>`).

Behind a proxy or load balancer, Traefik sees the proxy's address, so allowlist the proxy or turn the Automaton jails off (`enabled = false` in a `jail.local` section for each of them).

## Out of scope

- Public multi-tenant hosting: letting strangers run code on a shared machine.
- Protection against someone with root on your server or Runner machines, or with an administrator account.
- Your hosting provider, DNS and domain, and the apps you deploy (their code, dependencies and authentication).
- Off-server backups: nightly backups stay on the server; copies elsewhere are yours to arrange (see [backups and restore](./backup-restore)).
