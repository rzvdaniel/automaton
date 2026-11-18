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

     The first time, create `/usr/local/bin/automaton-deploy` (root-owned, mode 755) as below; after that, `update.sh` keeps it current. It accepts only the workflow's commands:

     ```bash
     #!/usr/bin/env bash
     set -euo pipefail
     [[ "${SSH_ORIGINAL_COMMAND:-}" =~ ^sudo\ /usr/bin/bash\ /opt/automaton-server/update\.sh\ --version\ server-v[0-9]+\.[0-9]+\.[0-9]+$ \
       || "${SSH_ORIGINAL_COMMAND:-}" =~ ^sudo\ /usr/local/bin/automaton-deploy-website\ [0-9]+\.[0-9]+\.[0-9]+$ ]] \
       || { echo "refused"; exit 1; }
     exec $SSH_ORIGINAL_COMMAND
     ```

     **The website step sets itself up.** Nothing to install by hand: `update.sh` (root, from the sha256-verified release bundle, never from another download) installs, only on a host that has a `website-green` container, `/usr/local/bin/automaton-deploy-website` (755), `/usr/local/lib/automaton/docker-compose.website.yml` (644) and `/etc/sudoers.d/automaton-deploy-website` (440, checked with `visudo -cf` first). The sudoers line has no argument wildcard (sudo-rs rejects it); the forced command and the script itself both require a plain `X.Y.Z` version. The sudoers user is the one account whose `authorized_keys` has the key locked to `/usr/local/bin/automaton-deploy`. If `/usr/local/bin/automaton-deploy` exists, it is rewritten atomically with the two regexes above. Re-runs change nothing that is current; a failure only warns, and the next update retries. In **Deploy production** `update.sh` runs before the website step, so the first deploy of the release that adds this sets it up and the website step works in the same run. Self-hosted servers are not changed.

     The script resolves tag `server-v<version>` to a commit, asks GitHub whether that commit is on `main`, and downloads the source by that exact sha (never by tag name, over HTTPS only). The source is only the Docker build context: the script and compose file that run are the root-owned ones, which define `website-green` alone and attach it to the existing `automaton-prod_automaton-prod` network (check the name with `docker network ls`). It builds and restarts only `website-green` (`--no-deps`) and never checks out or pulls in `/opt/automaton`. Recommended extra protection, your decision: a GitHub tag ruleset for `server-v*` so only the release workflow can create or move those tags. Check the script with `bash scripts/tests/deploy-website-test.sh`.
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
