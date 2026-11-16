---
title: Add a Runner
description: Install a Machine Management Runner on a Linux host with the CLI so Automaton can run your apps.
order: 60
---

# Add a Runner

A Runner is the trusted service on a machine that builds and runs your apps in Docker. Every app needs one. This page installs one from the CLI.

## Prerequisites

- A server with an administrator ([first administrator](./first-admin)) and the CLI signed in: `automaton login --server https://api.example.com` (see [Install with your AI agent](./install-with-ai) or [Your first app](./getting-started)).
- An x86_64 Linux host running **Ubuntu or Debian** (apt based, systemd) with Docker Engine running. It can be the Automaton server itself or another machine. The install does not check the distribution name: it needs a Linux host, a running Docker daemon and `/var/run/docker.sock`, so other distributions with Docker may work but are not tested. Otherwise it stops and names what is missing.
- The Runner controls the host's Docker. Install it only on machines you trust.

## Steps

The `automaton` commands below need a signed-in CLI. On a host without a browser run `automaton login --device --server https://api.example.com`, then approve the printed URL and code from a signed-in session or with the terminal snippet in [Log in without a browser](./automaton-cli#log-in-without-a-browser). The `--device` output contains the code: do not share it.

Pick the one that matches where you run the command.

```bash
# On the Runner host itself (same host as the server: the usual case)
automaton runners install --name main --local --yes

# From your computer, over SSH
automaton runners install --name main --ssh user@host

# Print the one-time command and run it yourself before it expires
automaton runners install --name main --print-command
```

To remove a Runner over SSH, run `automaton runners delete RUNNER --ssh user@host`; it runs the uninstall on the host and then deletes the registration.

The CLI asks you to confirm the Docker access. Add `--yes` for automation (required when not interactive). Add `--project NAME` to also add the Runner to a project. The command waits up to 300 seconds for the Runner to connect (`--timeout` changes this) and prints its Runner ID. `--print-command` also asks for confirmation, so give it `--yes` when there is no terminal, and it then waits for the Runner to enroll: run the printed command on the host within `--timeout` seconds (default 300), or the CLI times out. Use `--json` with `--ssh` or `--local` for `{runnerId, name, status}`.

You can also use Studio: **Runners → Install Machine Management Runner**.

## For AI agents

On the server host, in the shell used for the install, with the CLI signed in:

```bash
automaton runners install --local --yes --name main
```

Check: `automaton runners` lists `main` as connected or online (about 30 s).

## Verify it worked

```bash
automaton runners
```

Expected: your Runner listed as online. (The CLI says "connected" where Studio says "online"; both mean the Runner is reachable.) Studio shows the same under **Runners**.

With exactly one online Runner, `automaton deploy` uses it automatically. With several, choose with `automaton deploy --runner NAME` or `automaton set-runner NAME`. List a project's Runners with `automaton project runners`.

## Common errors

- **Docker not running on the host.** Start Docker, rerun the command.
- **Times out waiting for connection.** Port 5671 on the `api` host must be reachable from the Runner machine. See [Install the server](./install-server).
- **Enrollment command expired.** Run `automaton runners install` again; each command is single use.

Studio shows each Runner's storage and last heartbeat; it is offline after about ten minutes without one. Add another machine by repeating the install with a distinct name; do not clone a Runner's configuration or volume. Moving an app to another Runner does not move its host-local data. The generated command checks Linux, Docker and the socket, pulls the Runner image, creates an installation-scoped workspace volume at `/automaton/storage`, and fails with a redacted log tail if the container does not stay running. ext4 is the tested baseline; XFS and other filesystems with a working Docker are fine. Check free space on Docker's data filesystem, not just `/`.

Runners on other machines get `api.<domain>:5671` (TLS, certificate from the same Traefik resolver as the API) as their broker, with no configuration. Fresh installs write it; `update.sh` replaces a private address (an IP or single-label host) in `HostedRunnerRabbitMq` with it and keeps a custom public hostname. Port 5671 must be open on the public address.

If an app's Runner does not answer within 60 seconds, starting Actions and reading Action logs return **503** "It may be offline"; start the Runner and retry. A scheduled run that cannot reach it is recorded as failed.

Upgrading: a Runner on an old image shows **Update available**; its owner selects **Upgrade** (refused while a deploy or Action runs, or when offline). It starts a helper container that pulls the server's configured image and recreates the Runner with the same identity, configuration and volume; apps keep running, and if the new container does not connect the previous one is restored. Use `automaton runners upgrade RUNNER --wait` from the CLI, or **Run it manually** for an offline or very old Runner. Do not rerun the install command as an upgrade.

The trust boundary is in [Security overview](./security#runners-and-the-docker-socket). Next: [deploy an app](./deploy).
