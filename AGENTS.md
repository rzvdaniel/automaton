# AGENTS.md

This file is the entry point for coding agents working on Automaton.

Automaton is an AI-first platform for building, deploying, publishing, and running web apps. Keep changes focused, preserve existing behavior outside the task, and add regression coverage when practical.

## Repository skills

Read the matching skill before changing the related area:

- [Generic runtime](.agents/skills/generic-runtime/SKILL.md) for app source and runtime behavior, deployment contracts, public serving, archives, forks, and remixes.
- [Production deployment](.agents/skills/production-deployment/SKILL.md) for production scripts, Compose, blue-green cutovers, Traefik, certificates, health checks, and rollback.
- [Admin docs](.agents/skills/admin-docs/SKILL.md) for administrator-visible Studio or API behavior and its documentation.

The `.agents/skills/` directory also contains developer-local skills. Do not edit or add those skills unless the task names them.

## Agent orchestration

Symphony is the orchestration system for repository coding agents. Do not add manager/developer/reviewer role prompts, agent loops, orchestration state, metrics harnesses, or equivalent machinery under `.agents/`.

## Core rules

- Do not edit unrelated code, generated output, runtime state, secrets, `.env` files, local databases, or build artifacts.
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
- CLI: `src/Automaton.Cli/`
- Runner: `src/Automaton.Runner/`
- Shared infrastructure and identity: `src/Automaton.Infrastructure/` and `src/Automaton.Identity/`
- Tests: `src/UnitTests/`

## App source and runtime model

- Editable app content belongs under `source/`.
- Deployed output belongs under `live/` and must not be edited as source.
- Do not introduce language-, framework-, frontend-, or backend-specific assumptions into generic runtime boundaries.
- Legacy role-specific paths and controller behavior are compatibility surfaces only. Do not extend them into new generic-runtime architecture.
- Public serving must stay within the intended application runtime boundary and must not expose secrets, logs, runtime state, durable private data, or other unintended files.
- Do not copy deployed runtime output during fork or remix.
- Preserve `automaton.json` link metadata.

## Runtime and deployment invariants

- Within the application source payload, Automaton does not prescribe a language, framework, package manager, internal source layout, or application architecture.
- A web application owns its HTTP-serving runtime and declares it through the generic application contract.
- Runtime processes use the effective `AUTOMATON_PORT` and durable data belongs under `AUTOMATON_APP_DATA_DIR`.
- Build and runtime artifacts are disposable. Durable data must remain in the configured storage directory.
- Structured commands are preferred. Managed paths must stay inside the application workspace.
- Deployment is asynchronous. Do not claim success until the operation reaches `Succeeded` and the deployed URL is verified.

## Administrator documentation

When a change affects administrator-visible Studio or API behavior, update the relevant Admin documentation and its navigation in the same change. Use implemented routes, labels, permissions, commands, statuses, warnings, and consequences. If no documentation change is needed, explain why in the handoff.

## Verification

Use the affected project test and build commands. Common checks are:

```text
dotnet test
dotnet build -c Release
cd src/automaton-studio-vue && npm run type-check && npm run build
cd src/automaton-vue && npm run build
git diff --check
```

For production operations, follow the preflight, fast-forward, affected-service, blue-green, and verification workflow in the production deployment skill. Do not deploy or change production data without an explicit request.
