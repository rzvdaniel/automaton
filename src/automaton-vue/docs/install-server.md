---
title: Install Automaton on your server
description: Install your own Automaton Studio and API, connect a Runner, and update your installation.
---

# Install on your server

Automaton is open source under Apache 2.0. You run your own Studio, API and app workloads. No Automaton cloud account or subscription is required; you cover infrastructure and any AI tools you choose.

Already have a team installation? Ask your administrator for its API URL and go to [Getting started](./getting-started).

## Before starting

- An x86_64 Ubuntu 24.04+ server with sudo access and at least 10 GiB free disk space. Size memory, CPU and app storage for your workloads; this disk figure is installation headroom.
- A domain with `api`, `studio`, `store`, `apps` and `*.apps` DNS records pointing to the server. `store` serves your public app store.
- Inbound TCP ports 80 and 443 for HTTPS, and 5671 for the TLS Runner broker. The API hostname must remain directly reachable on the broker port.

The server currently requires public DNS and HTTPS. A private-only, offline or ARM installation is not the supported quickstart. Docker is installed automatically if missing.

## Install

Download and review the installer, then run it:

```bash
curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/install-automaton.sh -o install-automaton.sh
less install-automaton.sh
sudo bash install-automaton.sh
```

It asks for your domain and certificate email, downloads the latest stable release, verifies its checksum, and starts Studio, API, the app store, Worker, MySQL, RabbitMQ and Traefik. Add `--version server-vX.Y.Z` to select a release. Use `--tls-mode cloudflare` for optional Cloudflare wildcard certificates; provide the scoped DNS token only at the hidden terminal prompt.

## Create the administrator and connect a Runner

1. Open `https://studio.your-domain.com/install`.
2. On the server, read `sudo cat /etc/automaton/secrets/setup-token` and paste it into the setup page. Create your administrator, then sign in.
3. In **Runners**, create a Machine Management Runner installation and run its one-time command on the workload machine. You can use the same server or another supported machine. This trusted management component controls host Docker; ordinary apps do not receive its Docker socket or credentials.

Every app needs a Runner. Static sites share a static serving container; APIs and Actions use their declared runtime or execution containers.

## Connect the CLI

On your development computer, install the CLI and authenticate against **your API URL**:

```bash
dotnet tool install -g Automaton.Cli
automaton login --server https://api.your-domain.com
```

The NuGet tool requires the .NET 9 SDK or later. Approve login in your own Studio. Keep credentials out of AI chat. Continue with [your first app](./getting-started).

## Update

```bash
sudo bash /opt/automaton-server/update.sh
```

The updater selects the latest compatible stable release, checks its checksum, creates a local recovery backup, then replaces API, Worker, Studio and the app store. Updating to the first release with the app store needs the new updater once; see the complete guide linked below. These services can briefly be unavailable. Infrastructure changes and Runner-machine upgrades are separate operations; app workloads are not automatically moved to another server.

Keep a recovery plan. Local updater backups do not protect against losing the host, and no remote backup service is configured automatically. Preserve the database, app data, configuration and matching Data Protection keys together.

See the [complete installation, update and recovery guide](https://github.com/rzvdaniel/automaton/blob/main/docs/prod/install-automaton.md) for networking, custom paths, protected settings, adopted installations and recovery limits.
