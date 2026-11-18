---
title: Add a Runner
description: Install, upgrade and remove a Machine Management Runner on a Linux server or PC, or on Windows with WSL, so Automaton can run your apps.
order: 60
---

# Add a Runner

A Runner is the trusted service on a machine that builds and runs your apps in Docker. Every app needs one. This page installs, upgrades and removes one from the CLI.

## What to ask the user

Ask only what you cannot find out:

- **Which machine**: the Automaton server itself, another Linux server or PC, or a Windows PC.
- For another machine: its address and SSH login (`user@host`), and whether that user is root or has passwordless sudo. SSH must work with a key, without a password prompt.
- A name for the Runner (default `main`), and a project to add it to, if any.

Never ask for, print or store: an SSH, sudo or administrator password, an API key or token, the `--device` code, or the one-time install command (it is a secret until it expires).

## Prerequisites

- A server with an administrator ([first administrator](./first-admin)) and the CLI signed in: `automaton login --server https://api.example.com` (see [Install with your AI agent](./install-with-ai) or [Your first app](./getting-started)). Check: `automaton whoami` names the right server.
- An x86_64 Linux host (Ubuntu or Debian, systemd) running Docker Engine. It can be the server itself or another machine. Other distributions with Docker may work but are not tested. The Runner controls the host's Docker: install it only on machines you trust.
- **Root or passwordless sudo on the host.** The installer needs it. Check on the host: `docker info` works, `uname -m` prints `x86_64`, and `sudo -n true` succeeds (or you are root). `--local` and `--ssh` run the installer as `sudo bash -s`, so `sudo` must exist even for root. Without passwordless sudo, `--local` and `--ssh` fail with "sudo: ... can't do that": use `--print-command` instead. Root is only needed when the user cannot reach `/var/run/docker.sock`.
- **`--ssh` has no key option.** It runs plain `ssh -T -- user@host` without a terminal, so the key must work without any prompt: the default key (`~/.ssh/id_*`), a loaded `ssh-agent`, or a `Host` entry in `~/.ssh/config` (`HostName`, `User`, `IdentityFile`). Check first: `ssh user@host sudo -n true`. To use another key, add it to `~/.ssh/config` and pass that Host name as `--ssh`.
- Port 5671 on the server's `api` host must be reachable from the Runner host ([Install the server](./install-server)).

## Steps

On a host without a browser, sign in first with `automaton login --device --server https://api.example.com` ([Log in without a browser](./automaton-cli#log-in-without-a-browser)).

Pick the one that matches where you run the command.

```bash
# On the Runner host itself (same host as the server: the usual case)
automaton runners install --name main --local --yes

# From your computer, over SSH
automaton runners install --name main --ssh user@host --yes

# No sudo, or you prefer to run it yourself: print the one-time command, run it on the host before it expires
automaton runners install --name main --print-command --yes
```

The CLI asks you to confirm the Docker access; `--yes` is required when there is no terminal. Add `--project NAME` to also add the Runner to a project. The command waits up to 300 seconds (`--timeout`) for the Runner to connect and prints its Runner ID. With `--print-command` the CLI keeps waiting: run the printed command on the host within `--timeout` seconds. Run the CLI in the background (`automaton runners install --name main --print-command --yes > install.log 2>&1 &`), read the command from `install.log`, run it on the host, then wait for the CLI to finish and check `automaton runners`. A Linux PC uses `--local`. `--json` with `--ssh` or `--local` prints `{runnerId, name, status}`. Studio does the same under **Runners → Install Machine Management Runner**.

### A Windows PC

Docker Desktop does not work here: the Runner fails with "Ports are not available ... can't bind". Use Ubuntu in WSL 2 with systemd and Docker Engine instead:

1. In PowerShell as administrator: `wsl --install -d Ubuntu`, then restart if asked. In Ubuntu, check `systemctl is-system-running` (if it says it is not booted with systemd, add `[boot]` / `systemd=true` to `/etc/wsl.conf`, then run `wsl --shutdown` in PowerShell and open Ubuntu again).
2. In Ubuntu, install Docker Engine (see docs.docker.com, "Install Docker Engine on Ubuntu") and check `docker info` and `sudo -n true`.
3. Install the Runner from inside Ubuntu with `--local`, or from PowerShell with `--ssh` into WSL (this needs an SSH server in Ubuntu, `sudo apt install openssh-server`, and a key login).
4. **WSL stops when idle, and the Runner with it.** Apps on this PC go offline when the PC sleeps or WSL shuts down. Keep a WSL window open (or `wsl -d Ubuntu sleep infinity` in a terminal) while apps must run, and say so to the user. A PC that is often off is a poor Runner: prefer a server.

### For AI agents

As root, in the shell used for the install, with the CLI signed in: `automaton runners install --local --yes --name main`. Check: `automaton runners` lists `main` as connected (about 30 s).

## Verify it worked

```bash
automaton runners
```

Expected: your Runner listed as `connected` (Studio says "online"; both mean reachable), about 30 s after the install. With exactly one online Runner, `automaton deploy` uses it automatically; with several, `automaton deploy --runner NAME` or `automaton set-runner NAME`. List a project's Runners with `automaton project runners`.

## Upgrade a Runner

A server update does not change installed Runners ([Update the server](./update-server)). Ask the user before upgrading: apps keep running, but it is refused while a deploy or Action runs, and when the Runner is offline.

```bash
automaton runners upgrade main --wait
```

It prints "already uses the current image" when there is nothing to do. If the new container does not connect, the previous one is restored and the command exits non-zero. In Studio: **Runners → Upgrade**. For an offline or very old Runner use **Run it manually** there. Do not rerun the install command as an upgrade.

## Remove a Runner

Ask the user to confirm: apps on that Runner stop and lose their host-local data, so delete them or deploy them elsewhere first (`automaton apps`).

```bash
automaton runners delete main --ssh user@host      # runs the uninstall over SSH, then deletes the registration
```

`--ssh` needs the same root or passwordless sudo and key login as the install (see Prerequisites). Without `--ssh` (for example no sudo): `automaton runners delete main --json` prints `uninstallCommand` (copy it from `--json`; the plain output is wrapped at the terminal width and breaks when pasted), run it on the host as root, then `automaton runners delete main --host-cleanup-confirmed`. Shortcut: `automaton runners delete main --json | jq -r .uninstallCommand`. Run the uninstall as non-root and it ends with `rm: Permission denied` but exit 0: the folder is left behind, so rerun it as root. `automaton runners delete main --json` only prepares the removal: it marks the Runner for removal (default placement stops choosing it) and prints `uninstallCommand` with `deleted: false`; the registration goes after the uninstall and `--host-cleanup-confirmed`. If apps are still assigned it answers 409 "Move or delete all assigned app environments": move or delete them first. A new Runner can be assigned to apps by default placement, so check `automaton apps` after adding one. The `--host-cleanup-confirmed` flag only tells the server the uninstall ran; it does not touch the host, and it answers 409 "still online" while the Runner still connects. In Studio: **Runners**, remove the Runner; it shows the same uninstall command, then tick "I ran the command successfully" and **Confirm removal**.

The uninstall removes the Runner's containers and its installation folder. Docker itself, images already pulled and your own files on the host stay. Servers released up to 0.2.130 leave `/opt/automaton/self-hosted-runners/...`; remove it by hand.

Check: `automaton runners` no longer lists it, and `docker ps` on the host shows no container named `automaton-self-hosted-*` for it.

## Common errors

- **`sudo: ... password is required` / installer exits 1.** The SSH user has no passwordless sudo. Use root, or `--print-command`.
- **`rm: Permission denied` after the uninstall.** It was run without root and exited 0. Rerun as root.
- **Docker not running on the host.** Start Docker, rerun the command.
- **"Ports are not available" (Windows).** Docker Desktop: use WSL with Docker Engine, above.
- **Times out waiting for connection.** Port 5671 must be reachable from the Runner machine. See [Install the server](./install-server).
- **Enrollment command expired.** Run `automaton runners install` again; each command is single use.

Studio shows each Runner's storage and last heartbeat; it is offline after about ten minutes without one. Add another machine by repeating the install with a distinct name; do not clone a Runner's configuration or volume. Moving an app to another Runner does not move its host-local data. The generated command checks Linux, Docker and the socket, pulls the Runner image, creates an installation-scoped workspace volume at `/automaton/storage`, and fails with a redacted log tail if the container does not stay running. ext4 is the tested baseline; XFS and other filesystems with a working Docker are fine. Check free space on Docker's data filesystem, not just `/`.

Runners on other machines get `api.<domain>:5671` (TLS, certificate from the same Traefik resolver as the API) as their broker, with no configuration. Fresh installs write it; `update.sh` replaces a private address (an IP or single-label host) in `HostedRunnerRabbitMq` with it and keeps a custom public hostname. Port 5671 must be open on the public address.

If an app's Runner does not answer within 60 seconds, starting Actions and reading Action logs return **503** "It may be offline"; start the Runner and retry. A scheduled run that cannot reach it is recorded as failed.

## Environments and Runner assignments

Every app has one **Live** environment (stored internally as a deployment target). You can add named environments, for example `staging`, and each one has its own Runner assignment, deployed files, process, logs and private address. Environments can share a Runner without sharing runtime state.

- `automaton deploy` deploys Live; `automaton deploy staging` deploys the environment with that slug.
- Manage them in Studio under **Apps → open an app → Settings → Environments**: deploy, open the preview, view logs, rename, change the Runner, promote an additional environment to Live, or delete it. Promoting changes which running copy answers the app's normal and public addresses; it does not redeploy or reassign anything. The Live environment cannot be deleted until another one is promoted.
- Changing the Runner of a deployed environment asks for confirmation, because its runtime on the old Runner is cleaned up. Other environments are not touched.
- When Live has no Runner and exactly one online Runner is available in the app's scope (personal or project), `automaton deploy` assigns it and says so. Otherwise assign one: `automaton set-runner NAME` for Live, or the Runner picker in Studio.
- A project member with a Developer API key can use `automaton project runners list|add|remove` to manage a project's Runner links.

Environment status in Studio: **Unassigned** (no Runner), **Not deployed**, **Deploying** (a deploy not finished by the Runner after 30 minutes is marked Failed; the owner can cancel it), **Running**, **Failed**, **Runner offline**. A failed deploy leaves the previous healthy runtime serving.

Studio can also deploy the source the server stores for an app: **Apps → open an app → Overview → Deploy**. It refuses apps that deploy a prebuilt artifact (a runtime command with no build command); deploy those with the CLI.

## Deleting an app

**Apps → open an app → Settings → Danger Zone → Delete App** removes the app's runtimes, workspace, staging and persistent data, including its Compose volumes. Every assigned Runner must be online. If cleanup fails the app is kept: fix the reported problem and retry. Deleting an additional environment keeps the app's shared data. If an interrupted deletion stays blocked, an administrator can retry it with `DELETE /api/Apps/{appId}?recoverInterrupted=true`; if the Runner is permanently gone and you have removed its containers and storage yourself, add `&abandonOfflineRuntimes=true`.

The trust boundary is in [Security overview](./security#runners-and-the-docker-socket). Next: [deploy an app](./deploy).
