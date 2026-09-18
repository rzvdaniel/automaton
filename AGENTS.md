# AGENTS.md

This file is the platform contract for coding agents working in the Automaton repository.

Automaton is an AI-first platform for building, deploying, publishing, and running web apps. Keep changes focused, preserve existing behavior outside the task, and add regression coverage when practical.

This is not the `AGENTS.md` the CLI generates inside an app folder. For hosted-app work, follow that app's generated guidance.

## Repository skills

Read the matching skill before changing the related area:

| If the task touches… | Read |
| --- | --- |
| App source/runtime, `source/` / `live/`, serving, archives, forks, or remixes | [Generic runtime](.agents/skills/generic-runtime/SKILL.md) |
| Production scripts, Compose, Traefik, certificates, health checks, blue-green, or rollback | [Production deployment](.agents/skills/production-deployment/SKILL.md) |
| Docker disk space on a production host | [Production Docker maintenance](.agents/skills/production-docker-maintenance/SKILL.md) |
| Administrator-visible Studio or API behavior and its documentation | [Admin docs](.agents/skills/admin-docs/SKILL.md) |
| User-facing Studio UI, theme tokens, or visual verification | [Studio design](.agents/skills/studio-design/SKILL.md) |

The skills listed above are the repository contract. Other folders under `.agents/skills/` are developer-local. Do not add, expand, or commit local skills unless the task names that work.

## Agent orchestration

Symphony is the orchestration system for repository coding agents. Do not add manager/developer/reviewer role prompts, agent loops, orchestration state, metrics harnesses, or equivalent machinery under `.agents/` or `AgentRuns/`.

## Git workflow

Use the existing checkout for Automaton work. Do not create or move work into another Git worktree unless the user explicitly asks. When a task needs isolation, use a branch in the existing checkout. Check `git status` before switching branches and preserve uncommitted changes.

## Core rules

- Do not edit unrelated code, generated output, runtime state, secrets, `.env` files, local databases, or build artifacts.
- Do not edit `AgentRuns/`, `runner-data/`, `.local-dev/`, `.local-runtime/`, or `.prod-runtime/`.
- Do not rename public concepts, routes, or APIs unless the task requires it.
- Do not hardcode deployment-specific domains, ports, filesystem roots, limits, or infrastructure names. Use configured values and existing options.
- Keep controllers and UI components focused. Keep API access in service modules and use typed models.
- Use async APIs and cancellation tokens where the surrounding code does.
- Never log or commit credentials, access tokens, API keys, connection strings, or raw secrets.
- Run the smallest relevant verification after each change. Report unavailable checks exactly.

## Product surfaces

- Website: `src/automaton-vue/`
- Studio frontend: `src/automaton-studio-vue/`
- Studio backend: `src/Automaton.Api/`
- Worker: `src/Automaton.Worker/`
- CLI: `src/Automaton.Cli/`
- Runner: `src/Automaton.Runner/`
- Shared infrastructure and identity: `src/Automaton.Infrastructure/` and `src/Automaton.Identity/`
- Admin documentation: `src/Automaton.Api/Documentation/Admin/`
- Production and local scripts: `scripts/`
- Tests: `src/UnitTests/`

Local repository setup is the README quickstart. Do not invent Compose files, ports, or domains.

Implemented code, tests, and Admin docs are current behavior. Files under `docs/architecture/` are inventories and plans. Do not implement a later stage unless the task names it.

## Platform invariants

- Do not introduce language-, framework-, frontend-, or backend-specific assumptions into generic runtime boundaries.
- Legacy role-specific paths and controller behavior are compatibility surfaces only. Do not extend them into new generic-runtime architecture.
- Public serving must stay within the intended application runtime boundary and must not expose secrets, logs, runtime state, durable private data, or other unintended files.

For `source/` vs `live/`, forks, remixes, the application contract, and deployment success rules, read the generic-runtime skill.

When a change affects administrator-visible Studio or API behavior, follow the admin-docs skill in the same change. If no documentation change is needed, explain why in the handoff.

## Verification

Prefer the affected project, test filter, or script. The commands below are examples, not a mandatory checklist:

```text
dotnet test
dotnet build -c Release
cd src/automaton-studio-vue && npm run type-check && npm run build
cd src/automaton-vue && npm run build
git diff --check
```

For production shell changes, run `bash -n` on every touched script. For Studio UI, follow the studio-design visual checklist in addition to type-check.

For production operations, follow the preflight, fast-forward, affected-service, blue-green, and verification workflow in the production deployment skill. Do not deploy or change production data without an explicit request.
