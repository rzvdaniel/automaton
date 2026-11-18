# Deploy production from GitHub

The **Deploy production** workflow (`.github/workflows/deploy-production.yml`) updates the production server over SSH after the owner approves. The run page shows the version, who started it and who approved it. Agents never hold the production key: it lives only in the `production` environment.

After every run, approved or not, a final **Notify the owner** job opens an issue assigned to the repository owner ("Production deploy X: OK" or "needs attention") with the version, the AI check and docs walk results, the pull requests shipped and the deploy result, so GitHub sends the owner an email or push. It needs no secret. Close these issues when read.

It runs `sudo /usr/bin/bash /opt/automaton-server/update.sh --version server-v<version>` on the server, then rebuilds and restarts the marketing website (`website-green`) from the released commit, then checks that `https://api.automaton.monster/health`, Studio, the store and the website return 200.

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
     [[ "${SSH_ORIGINAL_COMMAND:-}" =~ ^sudo\ /usr/bin/bash\ /opt/automaton-server/update\.sh\ --version\ server-v[0-9]+\.[0-9]+\.[0-9]+$ \
       || "${SSH_ORIGINAL_COMMAND:-}" =~ ^sudo\ /usr/local/bin/automaton-deploy-website\ [0-9]+\.[0-9]+\.[0-9]+$ ]] \
       || { echo "refused"; exit 1; }
     exec $SSH_ORIGINAL_COMMAND
     ```

     The website step also needs `/usr/local/bin/automaton-deploy-website` (root-owned, mode 755, allowed in that user's sudoers like `update.sh`). It ignores stdin and downloads the source of tag `server-v<version>` from GitHub itself, so the SSH key cannot make it run anything else as root. It runs the archive's `scripts/deploy-website-prod.sh`, which builds and restarts only `website-green` (`--no-deps`). It never checks out or pulls in `/opt/automaton`:

     ```bash
     #!/usr/bin/env bash
     set -euo pipefail
     [[ "${1:-}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "refused"; exit 1; }
     dir="$(mktemp -d)"; trap 'rm -rf "$dir"' EXIT
     curl -fsSL --max-time 120 "https://github.com/rzvdaniel/automaton/archive/refs/tags/server-v$1.tar.gz" \
       | tar -xz --strip-components=1 --no-same-owner --no-same-permissions -C "$dir"
     bash "$dir/scripts/deploy-website-prod.sh" "$dir" "$1"
     ```
   - `PROD_SSH_KNOWN_HOSTS`: the server's host key line, from `ssh-keyscan -t ed25519 <host>` checked against the real server.
5. Known limit: an agent working on your machine under your own GitHub login (not the bot) can start and approve a run as you. Only approve runs you started or asked for, and check the version and the actor on the run page.
6. Keep agent accounts without admin rights, so they cannot edit environments or secrets. Changes to `.github/workflows/` are High tier and listed as risky in the release report you read before approving a deploy.

## Run it

- **Browser:** Actions → Deploy production → Run workflow → branch `main`, version such as `0.2.103` → Run. Open the run and **Review deployments → Approve**.
- **GitHub phone app:** open the repository → Actions → Deploy production → ⋯ / Run workflow, enter the version. When the run waits, you get a notification: open it and tap **Review deployments → Approve**.

Run the version on the test server first. A failed update keeps the previous release and its recovery backup; see [Update the server](../../src/automaton-vue/docs/install-server.md).

## Release report and AI check

Every server release carries a report as its GitHub Release body: an AI check (Green, Yellow or Red with short reasons), a "Risky" list, and one section per merged pull request (tier, review, security pass, proof, migrations, how to revert). The **Deploy production** run summary shows the verdict and links the report before you approve the `production` environment. Red is a warning, not a block.

The AI check needs the repository secret `CLAUDE_CODE_OAUTH_TOKEN` (a Claude subscription token from `claude setup-token`). Only the release workflow reads it. Without it, or when the usage limit is hit, the body says "AI check unavailable" and the release still publishes. Render a report locally with `python3 .agents/tools/release-report.py --from server-v0.2.104 --to server-v0.2.105`.

## Docs walk gate

**Deploy production** refuses a release unless its notes have a `## Docs walk` section with `Result: Pass` (the `report` job fails, so the environment approval never comes). The verifier writes it after walking the website docs on the test server: `python3 .agents/skills/automaton-verify-release/proof.py --docs-walk Pass <version> "note"`. To deploy anyway, start the run with `skip_docs_walk` set to the reason; the run summary shows it. Check the gate logic with `python3 .agents/tools/test_docs_walk_gate.py`.

## Publishing a server release

Run `.github/workflows/server-release.yml` on the reviewed revision with a version such as `0.1.0`. It builds and pushes the API, Worker and domain-independent Studio images to GHCR, keeps the previous infrastructure image digests, runs an isolated first-administrator smoke test, and publishes the bundle, checksum, installer and updater as `server-v0.1.0` plus a distribution image for anonymous updates. All four GHCR packages must be public (the workflow checks anonymous pulls; on first publication of the distribution package change its visibility, then rerun). Runner images are published separately by `runner-image.yml`; each server release pins the current `automaton-runner:latest` digest. The configuration tests and smoke test are development validation: accept a release on a disposable server (public DNS, certificates, clean Ubuntu with Docker) before calling it supported.
