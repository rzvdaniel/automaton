---
title: Install with your AI agent
description: Give an AI agent a server, a domain and an email. It installs Automaton, creates the administrator, adds a Runner and hands you a one-time link to choose your password.
---

# Install with your AI agent

An AI agent can take a fresh server to a working Automaton in 2-3 minutes. A person can run the same commands. Nothing else is needed.

## What you provide

1. **A server you can reach by SSH as root.** x86_64 Ubuntu 24.04, Ubuntu 22.04 or Debian 12, at least 2 GB RAM (4 GB recommended) and 10 GiB free disk, with ports 80, 443 and 5671 open.
2. **A domain with two DNS A records** pointing to the server's public IP, DNS only (no proxy, no orange cloud): `domain` and `*.domain`. Two records were enough in every test run.
3. **An email for Let's Encrypt.** It receives certificate notices and is not an account.

The agent asks which email to use for the administrator. Default: the certificate email.

## What the AI does

Run on the server as root. Use one shell for steps 4 to 6, because the generated password lives only in a shell variable. Never print, read back or write the password to a file. Do not paste the output of a step that contains a token or link into chat.

Set the inputs once:

```bash
DOMAIN=example.com
CERT_EMAIL=owner@example.com
EMAIL=$CERT_EMAIL        # administrator sign-in; use another email if the user asked
API=https://api.$DOMAIN
```

1. **Install `jq`.** Fresh Ubuntu and Debian do not include it. Install it visibly and check it; a quiet install can fail without a message.

   ```bash
   apt-get update && apt-get install -y jq curl python3
   jq --version
   ```

   Check: `jq --version` prints a version. If not, stop and fix apt.

2. **Install the server.** It checks DNS first and stops with the records to fix. It installs Docker itself (about 94 s on Debian 12). `--quiet` keeps the setup link out of the output.

   ```bash
   curl -fL https://raw.githubusercontent.com/rzvdaniel/automaton/main/scripts/install-automaton.sh -o install-automaton.sh
   bash install-automaton.sh --domain "$DOMAIN" --email "$CERT_EMAIL" --quiet
   ```

   Check: `curl -fsS "$API/health"` returns HTTP 200 with `"status":"ok"`. If DNS fails, tell the user which names do not resolve, wait, and rerun the same command.

3. **Create the administrator with a password nobody sees.** The password is generated into a variable. The setup token goes from its file into a header through a pipe.

   ```bash
   PASSWORD=$(printf 'Aa1!'; openssl rand -base64 24 | tr -d '\n=')
   jq -n --arg name "Administrator" --arg email "$EMAIL" --arg password "$PASSWORD" \
      '{name: $name, email: $email, password: $password}' |
     curl -s -w '\nHTTP %{http_code}\n' -X POST "$API/api/install" \
       -H @<(printf 'X-Automaton-Setup-Token: %s\n' "$(cat /etc/automaton/secrets/setup-token)") \
       -H 'Content-Type: application/json' --data-binary @-
   ```

   Check: the output ends with `HTTP 200`. `403` is a wrong token, `400` a weak password, `409` setup was already done. Details: [first administrator without a browser](./first-admin#without-a-browser-ssh-or-an-ai-agent).

4. **Install the .NET 10 SDK and the CLI.**

   ```bash
   curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
   bash dotnet-install.sh --channel 10.0
   export DOTNET_ROOT="$HOME/.dotnet" PATH="$PATH:$HOME/.dotnet:$HOME/.dotnet/tools"
   dotnet tool install -g Automaton.Cli
   ```

   Check: `automaton --help` prints usage.

5. **Sign the CLI in without a browser** with `automaton login --device --server "$API"`, then approve the printed request with the administrator's email and the password variable. Use the script in [Log in without a browser](./automaton-cli#log-in-without-a-browser) (it needs `EMAIL`, `PASSWORD` and `API` from above). Then `unset PASSWORD`.

   Check: `automaton whoami` shows `$API`. The approval call must return `200`.

6. **Add a Runner on the same host.**

   ```bash
   automaton runners install --local --yes --name main
   ```

   Check: `automaton runners` lists `main` as connected or online (about 30 s).

7. **Hand over the one-time link.** Run it last, so the link is fresh.

   ```bash
   "$(command -v automaton)" server password-link --email "$EMAIL"
   ```

   As a non-root user use `sudo "$(command -v automaton)" server password-link --email "$EMAIL"`. Show the printed link in your final message, together with `https://studio.$DOMAIN`. Check: the output is a link to Studio's set-password page.

Then continue with [Your first app](./getting-started) if the user asked for an app.

## What you get

A working server, its Studio at `https://studio.<domain>`, a Runner on the same host and a signed-in CLI for the agent.

1. Open the one-time link. It works once and for 30 minutes.
2. Choose your own password. The generated one stops working at that moment.
3. Sign in to Studio with the administrator email and your password. Change the password later in your account settings.
4. **The link expired or was lost?** Ask the agent to run step 7 again, or run it yourself on the server. Each new link voids the earlier ones. Creating and using links is limited to 5 per hour per IP.

## Common problems

- **DNS check fails.** Create the missing A records (`domain` and `*.domain`, DNS only) and rerun step 2.
- **`jq: command not found`.** Step 1 did not finish; rerun it and read its output.
- **`automaton: command not found`.** The `export` line in step 4 is missing in this shell.
- **Setup returns 409.** The administrator already exists. Use step 7 to give the user a link.
- **Certificate not issued.** Port 80 must be reachable. After Let's Encrypt `429`, wait or use other names. See [Install the server](./install-server#common-errors).
- **Link rejected or rate limited.** Wait and run step 7 again.
- More: [Troubleshooting](./troubleshooting), [Install the server](./install-server), [Add a Runner](./deploy-runner).
