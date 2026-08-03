---
name: automaton-production-deploy
description: Safely update the Automaton production deployment over SSH using the automaton host alias, preserving production-only state, selecting affected services, and completing blue-green health and public endpoint verification. Use when the user asks to deploy, update, redeploy, or roll out Automaton changes to production.
---

# Automaton Production Deploy

Use this skill only for an explicitly requested production deployment. The production checkout is `/opt/automaton` on the SSH host alias `automaton`; the SSH user is `automaton` and deployment commands use passwordless `sudo -n`.

## Safety rules

- Never reset, clean, stash, overwrite, or delete production checkout state without explicit approval.
- Treat `privatekey.xml`, `publickey.xml`, `.env`, `secrets/`, `venv/`, `backups/`, database files, generated runtime files, and test data as production state. Never stage or commit them.
- Inspect tracked changes before pulling. If tracked changes are not already represented by `origin/main`, stop and report them.
- Untracked production-only files may remain during a fast-forward pull; report that they were preserved.
- Use `sudo -n` so an unexpected password prompt fails safely.
- Keep the currently live blue-green color serving traffic until the replacement is healthy, reachable through Traefik, and warmed up.
- Do not claim a migration is complete merely because a build succeeded. Confirm it through the deployment logs or an explicit migration/status check.

## 1. Preflight

From the local Automaton checkout, verify SSH and inspect production without mutating it:

```powershell
ssh -o BatchMode=yes automaton "cd /opt/automaton && git status -sb && git log -1 --oneline --decorate && sudo -n bash scripts/prod-switch-blue-green.sh status && docker compose -f docker-compose.prod.yml ps"
```

If SSH authentication fails, do not retry deployment commands. The expected Windows OpenSSH setup is:

```powershell
Start-Service ssh-agent
ssh-add C:\Users\Razvan\.ssh\automaton
ssh-add -l
ssh automaton "hostname"
```

If the service is not persistent, an elevated PowerShell can run:

```powershell
Set-Service ssh-agent -StartupType Automatic
Start-Service ssh-agent
```

Record the current live colors, health of MySQL/RabbitMQ/Worker/Traefik, production HEAD, and any tracked or untracked checkout state.

## 2. Inspect and pull the requested revision

Fetch and compare before pulling:

```powershell
ssh automaton "cd /opt/automaton && sudo -n git fetch origin && git log --oneline --decorate -5 origin/main && git diff --stat HEAD..origin/main && git diff --name-status HEAD..origin/main"
```

If the user specified a revision other than `main`, substitute it deliberately and inspect that revision first. For the normal main deployment, update without creating a merge commit:

```powershell
ssh automaton "cd /opt/automaton && sudo -n git pull --ff-only origin main"
```

If the pull is not fast-forwardable, stop. Do not use reset or create a merge commit automatically.

## 3. Select redeployments from the merged diff

Redeploy only affected services:

| Change area | Action |
|---|---|
| `src/Automaton.Api/**`, `src/Automaton.Infrastructure/**`, migrations, shared backend, messaging | API |
| `src/automaton-studio-vue/**` | Studio |
| `src/Automaton.Worker/**` or Worker/messaging runtime behavior | Worker |
| `src/automaton-vue/**` | Website |
| Runner Dockerfile/image or runner runtime changes | Follow the repository’s current runner-image/provisioning procedure; do not invent a production service name |

External artifacts such as a previously published CLI NuGet package or Runner image do not by themselves require rebuilding an unrelated production container. Confirm that the deployed code references the intended package/image tag when that matters.

## 4. Redeploy with blue-green scripts

Run each selected service from `/opt/automaton`:

```powershell
ssh automaton "cd /opt/automaton && sudo -n ./scripts/prod-redeploy-api.sh"
ssh automaton "cd /opt/automaton && sudo -n ./scripts/prod-redeploy-studio.sh"
ssh automaton "cd /opt/automaton && sudo -n bash scripts/prod-redeploy-worker.sh"
ssh automaton "cd /opt/automaton && sudo -n ./scripts/prod-redeploy-website.sh"
```

Run them sequentially when more than one service is affected so each cutover is observable. A successful script must build the image, start the inactive color, pass health and Traefik reachability checks, warm the service, switch nginx traffic, and retire the previous color only after its overlap/drain period. If any script fails, stop and preserve the old live color for diagnosis or rollback.

For API changes with migrations, inspect the API startup output for migration execution and any data-safety warning. Never delete production rows to make a migration pass.

## 5. Final verification

Run the repository verification script:

```powershell
ssh automaton "cd /opt/automaton && sudo -n bash scripts/prod-verify.sh"
```

Then capture the final checkout and container state:

```powershell
ssh automaton "cd /opt/automaton && git status -sb && git log -1 --oneline --decorate && docker compose -f docker-compose.prod.yml ps"
```

The final handoff must include:

- pulled commit;
- tracked production changes preserved, reverted with approval, or blocking the deployment;
- production-only untracked state preserved;
- redeployed services and successful build, health, warmup, cutover, and drain checks;
- services intentionally not redeployed and why;
- migration status and any data-safety precondition;
- final live colors for API, Studio, and Website;
- `prod-verify.sh` result and any warnings.
