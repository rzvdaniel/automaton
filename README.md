# Automaton

**Build with AI. Deploy on your own server.**

Automaton is an open-source platform for deploying web apps, APIs and Actions on infrastructure you control. Build locally with your preferred coding agent, deploy with the CLI, and manage apps in Studio.

[Website](https://automaton.monster) · [Install guide](docs/prod/install-automaton.md) · [Releases](https://github.com/rzvdaniel/automaton/releases) · [App documentation](https://automaton.monster/docs/) · [Example apps](https://github.com/rzvdaniel/automaton-apps)

> **Early access.** The core app lifecycle has been exercised on our production server. Clean-server acceptance and recovery testing remain limited. Plan backups and a brief interruption during server updates.

## Install

Use an **x86_64 Ubuntu 24.04+ server**, a domain, and at least **10 GiB free disk space**. Configure `api`, `studio`, `store`, `apps` and `*.apps` DNS records; allow inbound TCP **80, 443 and 5671** (5671 is the TLS broker Runners connect to). The [install guide](docs/prod/install-automaton.md) explains certificates and networking.

```bash
curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/install-automaton.sh -o install-automaton.sh
less install-automaton.sh
sudo bash install-automaton.sh
```

The installer selects the latest stable release, verifies the bundle, installs Docker if needed, and starts the server. No source build or GitHub account is required.

1. Open `https://studio.your-domain.com/install`.
2. Read `sudo cat /etc/automaton/secrets/setup-token` on the server and create your administrator in Studio.
3. In **Runners**, install a Machine Management Runner on this machine or another server. Apps need a Runner, including static sites.

## Build and publish an app

Install the CLI on your development computer, then connect it to **your server**:

```bash
dotnet tool install -g Automaton.Cli
automaton login --server https://api.your-domain.com
mkdir my-app && cd my-app
automaton new "My App"
```

The CLI requires the .NET 9 SDK or later for NuGet installation. The server installer uses containers and does not require a separate .NET installation. See the [CLI guide](https://automaton.monster/cli).

Ask your coding agent to build the app. It should read the generated `AGENTS.md` and `automaton.json`, then validate, deploy and verify the real app behavior:

```bash
automaton validate
automaton deploy
automaton publish --yes
```

Approve CLI login in **your Studio** when prompted. Keep credentials out of AI chat.

### Example apps

[automaton-apps](https://github.com/rzvdaniel/automaton-apps) collects apps built and deployed on Automaton, such as [Untranslatable](https://untranslatable.apps.automaton.monster/), a multiplayer word-bluffing game.

## Update

```bash
sudo bash /opt/automaton-server/update.sh
```

The updater verifies the latest compatible release and makes a local recovery backup before replacing API, Worker, Studio and the app store. Updating to the first release with the app store needs the new updater once; see [updates and recovery](docs/prod/install-automaton.md#update-an-installed-server). These services can be briefly unavailable. Infrastructure upgrades and existing Runner updates require separate review; this command does not upgrade every machine automatically. See [updates and recovery](docs/prod/install-automaton.md#update-an-installed-server).

## What you get

- **Your Studio and API** on your domain, with app, user and access management.
- **Your app store** at `store.your-domain.com`, listing the apps their owners choose to publish.
- **Static sites, APIs and Actions** on your own machines. Static sites share a serving container; APIs and jobs use bounded containers.
- **Persistent app data** across redeploys, explicit machine placement and multiple Runner machines.
- **Your AI workflow** with CLI-generated agent instructions and deployment checks.

The software is free under [Apache 2.0](LICENSE). You pay for your servers, domain and any AI services you choose. There is no required Automaton cloud subscription. You operate the installation, updates and backups.

## Contribute

See [repository development](docs/development.md), the [roadmap](docs/roadmap/README.md), and [GitHub issues](https://github.com/rzvdaniel/automaton/issues). Coding agents should start with [AGENTS.md](AGENTS.md).
