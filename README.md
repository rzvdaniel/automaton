# Automaton

**Build with AI. Deploy on your own server.**

Automaton is an open-source platform for deploying web apps, APIs and Actions on infrastructure you control. Build locally with your preferred coding agent, deploy with the CLI, and manage apps in Studio.

[Website](https://automaton.monster) · [Install guide](docs/prod/install-automaton.md) · [Releases](https://github.com/rzvdaniel/automaton/releases) · [App documentation](https://automaton.monster/docs/) · [Example apps](https://github.com/rzvdaniel/automaton-apps)

> **Early access.** The app lifecycle runs on our own production server. Not yet proven: a restore onto a different clean server. A fresh server installs in 90 seconds (measured on a Hetzner CPX22). There is no high availability, and apps cannot use WebSockets. Updates briefly interrupt the services, and backups stay on the same server unless you copy them. See [current limitations](https://automaton.monster/docs/limitations).

## Is Automaton for you?

**It fits** if you or your small team build many small apps, tools or automations with an AI coding agent and want them on servers you control, at a cost set by the servers you rent, and you are comfortable owning a Linux server.

**It does not fit** if you need autoscaling, multi-region failover, live streaming connections, a hosted service to sign up for, or hosting for untrusted users.

Compared with a cloud provider you gain control and data location, with costs set by the servers you rent; you take on updates, backups and being on call, and you get no global scale. The full comparison, with pros and cons, is on [Is Automaton for you?](https://automaton.monster/docs/is-automaton-for-you). Questions: [FAQ](https://automaton.monster/docs/faq).

## Quickstart

1. Install the server on an Ubuntu 24.04+ host (below) and create your administrator in Studio.
2. Install the CLI, log in with `automaton login --server https://api.your-domain.com`, then add a Runner with `automaton runners install --name main --ssh user@host --yes` (or `--local` on the host). The Machine Management Runner has Docker access on its host.
3. Run `automaton new "My App"`.
4. Ask your coding agent to build the app, then `automaton validate` and `automaton deploy`.

## Install

Use an **x86_64 Ubuntu 24.04+ server**, a domain, and at least **10 GiB free disk space**. Configure `api`, `studio`, `store`, `apps` and `*.apps` DNS records; allow inbound TCP **80, 443 and 5671** (5671 is the TLS broker Runners connect to). The [install guide](docs/prod/install-automaton.md) explains certificates and networking.

```bash
curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/install-automaton.sh -o install-automaton.sh
less install-automaton.sh
sudo bash install-automaton.sh --domain your-domain.com --email you@your-domain.com
```

Without `--domain` and `--email` the installer asks for them; pass both when an AI agent runs the install.

The installer selects the latest stable release, verifies the bundle, installs Docker if needed, and starts the server. No source build or GitHub account is required.

1. Open `https://studio.your-domain.com/install`.
2. Read `sudo cat /etc/automaton/secrets/setup-token` on the server and create your administrator in Studio.
3. Use `automaton runners install --name main --local --yes` on this machine, or `--ssh user@host --yes` from another machine. Apps need a Runner, including static sites.

## Build and publish an app

Install the CLI on your development computer, then connect it to **your server**:

```bash
dotnet tool install -g Automaton.Cli
automaton login --server https://api.your-domain.com
mkdir my-app && cd my-app
automaton new "My App"
```

The CLI requires the .NET 10 SDK or later for NuGet installation. The server installer uses containers and does not require a separate .NET installation. See the [CLI guide](https://automaton.monster/cli).

Ask your coding agent to build the app. It should read the generated `AGENTS.md` and `automaton.json`, then validate, deploy and verify the real app behavior:

```bash
automaton validate
automaton deploy
automaton publish --yes --slug my-app
```

Approve CLI login in **your Studio** when prompted. Keep credentials out of AI chat.

### Example apps

[automaton-apps](https://github.com/rzvdaniel/automaton-apps) collects apps built and deployed on Automaton, such as [Untranslatable](https://untranslatable.apps.automaton.monster/), a multiplayer word-bluffing game.

## Update

```bash
sudo bash /opt/automaton-server/update.sh
```

The updater verifies the latest compatible release and makes a local recovery backup before replacing API, Worker, Studio and the app store. Updating to the first release with the app store needs the new updater once; see [updates and recovery](docs/prod/install-automaton.md#update-an-installed-server). These services can be briefly unavailable. Infrastructure upgrades and existing Runner updates require separate review; this command does not upgrade every machine automatically.

Your server backs itself up nightly (database and keys, 7 days, on the server). Also turn on your hosting provider's server backups; see [backups and restore](docs/prod/install-automaton.md#backups-and-restore).

## What you get

- **Your Studio and API** on your domain, with app, user and access management.
- **Your app store** at `store.your-domain.com`, listing the apps their owners choose to publish.
- **Static sites, APIs and Actions** on your own machines. Static sites share a serving container; APIs and jobs use bounded containers.
- **Persistent app data** across redeploys (not included in the nightly server backups), several Runner machines, and a choice of which Runner hosts each app.
- **Your AI workflow** with CLI-generated agent instructions and deployment checks.

The software is free under [Apache 2.0](LICENSE). You pay for your servers, domain and any AI services you choose. There is no required Automaton cloud subscription. You operate the installation and updates; nightly backups are set up for you, and off-server copies are yours to keep.

## Contribute

Report security problems privately as described in [SECURITY.md](SECURITY.md); see also the [security overview](docs/security/overview.md).

Ask questions in [Discussions](https://github.com/rzvdaniel/automaton/discussions), report bugs or request features with an [issue template](https://github.com/rzvdaniel/automaton/issues/new/choose), and read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. See also [repository development](docs/development.md) and the [roadmap](docs/roadmap/README.md). Coding agents should start with [AGENTS.md](AGENTS.md).
