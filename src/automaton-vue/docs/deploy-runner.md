---
title: Add a Runner
description: Install a Machine Management Runner on a Linux host with the CLI so Automaton can run your apps.
---

# Add a Runner

A Runner is the trusted service on a machine that builds and runs your apps in Docker. Every app needs one. This page installs one from the CLI.

## Prerequisites

- A server with an administrator ([first administrator](./first-admin)) and the CLI signed in: `automaton login --server https://api.example.com` (see [Create your first app](./getting-started)).
- An x86_64 Linux host running **Ubuntu or Debian** (apt based, systemd) with Docker Engine running. It can be the Automaton server itself or another machine. The install does not check the distribution name: it needs a Linux host, a running Docker daemon and `/var/run/docker.sock`, so other distributions with Docker may work but are not tested. Otherwise it stops and names what is missing.
- The Runner controls the host's Docker. Install it only on machines you trust.

## Steps

The `automaton` commands below need a signed-in CLI. On a host without a browser run `automaton login --device --server https://api.example.com`, then approve the printed URL and code from a signed-in session or with the terminal snippet in [Log in without a browser](./automaton-cli#log-in-without-a-browser). The `--device` output contains the code: do not share it.

Pick the one that matches where you run the command.

```bash
# On the Runner host itself
automaton runners install --name main --local

# From your computer, over SSH
automaton runners install --name main --ssh user@host

# Print the one-time command and run it yourself before it expires
automaton runners install --name main --print-command
```

The CLI asks you to confirm the Docker access. Add `--yes` for automation (required when not interactive). Add `--project NAME` to also add the Runner to a project. The command waits up to 300 seconds for the Runner to connect (`--timeout` changes this) and prints its Runner ID. `--print-command` also asks for confirmation, so give it `--yes` when there is no terminal, and it then waits for the Runner to enroll: run the printed command on the host within `--timeout` seconds (default 300), or the CLI times out. Use `--json` with `--ssh` or `--local` for `{runnerId, name, status}`.

You can also use Studio: **Runners → Install Machine Management Runner**.

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

Host requirements and the trust boundary are in the [Runner reference](https://github.com/rzvdaniel/automaton/blob/main/docs/prod/self-hosted-runners.md). Next: [deploy an app](./deploy).
