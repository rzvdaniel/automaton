# Deploy production from GitHub

The **Deploy production** workflow (`.github/workflows/deploy-production.yml`) updates the production server over SSH after the owner approves. The run page shows the version, who started it and who approved it. Agents never hold the production key: it lives only in the `production` environment.

It runs `sudo /usr/bin/bash /opt/automaton-server/update.sh --version server-v<version>` on the server, then checks that `https://api.automaton.monster/health`, Studio, the store and the website return 200.

## One-time setup (owner)

1. Repository **Settings → Environments → New environment**, name it `production`.
2. **Required reviewers:** add `rzvdaniel`. Leave "Prevent self-review" off if you also start the runs yourself, otherwise you cannot approve your own run.
3. **Deployment branches and tags:** choose "Selected branches and tags" and add `main`.
4. **Environment secrets** (not repository secrets):
   - `PROD_SSH_HOST`: the server's host name or IP.
   - `PROD_SSH_USER`: the SSH user, which may run `sudo /usr/bin/bash /opt/automaton-server/update.sh` without a password.
   - `PROD_SSH_KEY`: a private key dedicated to this workflow, with its public key in that user's `authorized_keys`. Lock that line to the update command, so a leaked key can do nothing else:

     ```
     restrict,command="/usr/local/bin/automaton-deploy" ssh-ed25519 AAAA... deploy-production
     ```

     `/usr/local/bin/automaton-deploy` (root-owned, mode 755) accepts only the workflow's command:

     ```bash
     #!/usr/bin/env bash
     set -euo pipefail
     [[ "${SSH_ORIGINAL_COMMAND:-}" =~ ^sudo\ /usr/bin/bash\ /opt/automaton-server/update\.sh\ --version\ server-v[0-9]+\.[0-9]+\.[0-9]+$ ]] \n       || { echo "refused"; exit 1; }
     exec $SSH_ORIGINAL_COMMAND
     ```
   - `PROD_SSH_KNOWN_HOSTS`: the server's host key line, from `ssh-keyscan -t ed25519 <host>` checked against the real server.
5. Known limit: an agent working on your machine under your own GitHub login (not the bot) can start and approve a run as you. Only approve runs you started or asked for, and check the version and the actor on the run page.
6. Keep agent accounts without admin rights, so they cannot edit environments or secrets. Changes to `.github/workflows/` are High tier and need your `owner-approved` label.

## Run it

- **Browser:** Actions → Deploy production → Run workflow → branch `main`, version such as `0.2.103` → Run. Open the run and **Review deployments → Approve**.
- **GitHub phone app:** open the repository → Actions → Deploy production → ⋯ / Run workflow, enter the version. When the run waits, you get a notification: open it and tap **Review deployments → Approve**.

Run the version on the test server first. A failed update keeps the previous release and its recovery backup; see [install-automaton.md](install-automaton.md).
