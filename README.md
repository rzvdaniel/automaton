# Automaton

Automaton is a self-hosted platform where you or your AI agent build, deploy and share small web apps and automations on servers you control.

## Who it is for

**Fits** if you:
- build many small apps, tools or automations, often with an AI coding agent;
- want them on servers you control, at a cost set by the servers you rent;
- are comfortable owning a Linux server (updates, backups, being on call).

**Does not fit** if you need:
- autoscaling or multi-region failover;
- WebSockets or streamed responses (apps cannot use them);
- a hosted service to sign up for, or hosting for untrusted users.

> **Early access.** The app lifecycle runs on our own production server. A restore from an off-site archive onto a clean second server has been proven. Nightly backups stay on the server by default; copy them and the backup key to encrypted off-site storage for recovery. Fresh-server installs took 118 seconds on Ubuntu 22.04 and about 90 seconds on Debian 12 (Hetzner CX23; [measurements](https://github.com/rzvdaniel/automaton/issues/561)). See [limitations](https://automaton.monster/docs/limitations), [Is Automaton for you?](https://automaton.monster/docs/is-automaton-for-you) and the [FAQ](https://automaton.monster/docs/faq).

## Quickstart

1. **Install the server** on an x86_64 Linux server with a domain ([install-server](https://automaton.monster/docs/install-server)). DNS only: three records (`*.your-domain.com`, `apps.your-domain.com` and `*.apps.your-domain.com`; the second is needed because the third hides `apps` from the first) or five named records (`api`, `studio`, `store`, `apps`, `*.apps`). Open TCP 80, 443 and 5671. Review the script before running it if you like (`less install-automaton.sh`).

   ```bash
   curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/install-automaton.sh -o install-automaton.sh && sudo bash install-automaton.sh --domain your-domain.com --email you@your-domain.com
   ```

2. **Create the first administrator** by opening the one-time setup link the installer prints ([first-admin](https://automaton.monster/docs/first-admin)).

3. **Install the CLI and log in** (needs the .NET 10 SDK; [automaton-cli](https://automaton.monster/docs/automaton-cli)):

   ```bash
   dotnet tool install -g Automaton.Cli
   automaton login --server https://api.your-domain.com
   ```

4. **Add a Runner**, which hosts your apps ([deploy-runner](https://automaton.monster/docs/deploy-runner)). Run on the server with `--local`, or from anywhere with `--ssh user@host`:

   ```bash
   automaton runners install --name main --local
   ```

5. **Deploy your first app** ([getting-started](https://automaton.monster/docs/getting-started), [deploy](https://automaton.monster/docs/deploy)):

   ```bash
   automaton new "My App" --template node
   automaton validate
   automaton deploy
   automaton publish --yes --slug my-app
   ```

6. **Update the server** later ([update-server](https://automaton.monster/docs/update-server)):

   ```bash
   automaton server update --wait
   ```

Stuck? See [troubleshooting](https://automaton.monster/docs/troubleshooting).

## For AI agents

- Start with [llms.txt](https://automaton.monster/llms.txt), or [llms-full.txt](https://automaton.monster/llms-full.txt) for all the docs in one file.
- In an app folder, read the generated `AGENTS.md` and `automaton.json` ([reference](https://automaton.monster/docs/automaton-json)), then validate, deploy and verify the real app.
- Safety: never put credentials in chat. Self sign-up is disabled; users come from an administrator. See [secrets and permissions](https://automaton.monster/docs/secrets-and-permissions) and [security](https://automaton.monster/docs/security).
- Working on this repository: read [AGENTS.md](AGENTS.md).

## Links

[Website](https://automaton.monster) · [Docs](https://automaton.monster/docs/) · [Example apps](https://github.com/rzvdaniel/automaton-apps) · [Releases](https://github.com/rzvdaniel/automaton/releases) · [Security](SECURITY.md) · [Contributing](CONTRIBUTING.md) · [License (Apache 2.0)](LICENSE)
