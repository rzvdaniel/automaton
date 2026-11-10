# AGENTS.md

The contract for coding agents working in the Automaton repository. Automaton is an AI-first platform for building, deploying, publishing and running web apps. Keep changes focused, preserve behavior outside the task, and add regression coverage when practical.

This is not the `AGENTS.md` the CLI generates inside an app folder. For hosted-app work, follow that app's generated guidance.

## Repository skills

Read the matching skill before changing the related area:

| If the task touches… | Read |
| --- | --- |
| Code structure: complexity, coupling, service responsibilities, type ownership, keeping changes small, analyzer builds | [Maintainability](.agents/skills/maintainability/SKILL.md) |
| App source/runtime, `source/` / `live/`, serving, archives, forks, remixes, runtime boundaries | [Generic runtime](.agents/skills/generic-runtime/SKILL.md) |
| Runtime placement, Runners, deployments, workers/messaging, storage, scheduling, routing, distributed state, infrastructure providers | [Distributed platform architecture](.agents/skills/distributed-platform-architecture/SKILL.md) |
| Production scripts, Compose, Traefik, certificates, health checks, blue-green, rollback, production updates | [Production deployment](.agents/skills/production-deployment/SKILL.md) |
| Fresh production installation, setup configuration, secrets, bootstrap | [Production installation setup](.agents/skills/production-installation-setup/SKILL.md) |
| Docker disk space on a production host | [Production Docker maintenance](.agents/skills/production-docker-maintenance/SKILL.md) |
| Administrator-visible Studio or API behavior and its documentation | [Admin docs](.agents/skills/admin-docs/SKILL.md) |
| Roadmaps, architecture guides, other repository documentation | [Documentation maintenance](.agents/skills/documentation-maintenance/SKILL.md) |
| User-facing Studio UI, theme tokens, visual verification, browser automation | [Studio design](.agents/skills/studio-design/SKILL.md) |
| Reviewing or merging any pull request | [PR review](.agents/skills/pr-review/SKILL.md) |
| Creating any GitHub issue | [Write issue](.agents/skills/automaton-write-issue/SKILL.md) |
| Opening any pull request | [Write PR](.agents/skills/automaton-write-pr/SKILL.md) |
| Implementing an issue end to end (issue, PR, review, merge, release, test-server verification), when the owner or orchestrator asks for it | [Automaton implement issue](.agents/skills/automaton-implement-issue/SKILL.md) |
| Only when the owner makes you the orchestrator of the agents | [Automaton orchestrate](.agents/skills/automaton-orchestrate/SKILL.md) |

These skills are the repository contract. Other folders under `.agents/skills/` are developer-local; do not add or commit skills, agent loops, role prompts or orchestration machinery unless the task names that work.

## Operating rules

- **Your environment.** The test server, production, boards and agents of your setup are named in `.agents/environment.md` (copy `.agents/environment.example.md`; not committed). Without a test server, test locally with the README quickstart or on your own install.
- **Servers.** Test only on the test server; agents there run as an unprivileged user, not root. Touch production only when the owner asks or has pre-approved it (releases and production updates of verified work). Always ask before deleting data, volumes, backups or the database, migrations that drop or rewrite data, and changes that widen access or change sign-in.
- **Sign-up** is disabled on purpose. Never re-enable it, insert users into the database, or bypass the invite flow.
- **Secrets.** Never print, log, commit or post credentials, tokens, API keys or connection strings. Test credentials live where `.agents/environment.md` says: read values literally (CRLF, values may contain `$`), never `source` them.
- **Issues and the board.** Issue titles say what the work does. Maintainers put each issue under a parent issue and on the project board (`.agents/environment.md`); contributors without board access just file the issue. Changing a single-select field's options through the API clears it on every item: save the values first and set them again afterwards.
- **Git.** Work on a branch (docs-only changes may go straight to `main` when the owner says so). Check `git status` before switching branches and keep the main folder on `main` between tasks.
- **Attribution.** Agents use the owner's GitHub login and end commit messages and PR descriptions with a line naming the agent.
- **Finish what you start, or leave it clearly open:** report what was verified and what wasn't, and post anything a later agent needs on the issue.

## Core rules

- Do not edit unrelated code, generated output, runtime state, secrets, `.env` files, local databases, build artifacts, `AgentRuns/`, `runner-data/`, `.local-dev/`, `.local-runtime/` or `.prod-runtime/`.
- Do not rename public concepts, routes or APIs unless the task requires it.
- Do not hardcode deployment-specific domains, ports, filesystem roots, limits or infrastructure names.
- Choose the smallest complete change through existing code paths; no speculative abstractions or future stages (details in the maintainability skill).
- Implemented code, tests and Admin docs are current behavior. Keep the relevant guide under `docs/architecture/` up to date when architecture, state ownership, message flow or runtime boundaries change.
- Run the smallest relevant check after each change (`bash -n` for shell scripts, type-check for Studio, the affected .NET project or test filter). Report checks you could not run exactly.

## Product surfaces

- Website: `src/automaton-vue/`
- Studio frontend: `src/automaton-studio-vue/`
- Studio backend: `src/Automaton.Api/`
- Worker: `src/Automaton.Worker/`
- CLI: `src/Automaton.Cli/`
- Runner: `src/Automaton.Runner/`
- Shared infrastructure and identity: `src/Automaton.Infrastructure/`, `src/Automaton.Identity/`
- Admin documentation: `src/Automaton.Api/Documentation/Admin/`
- Production and local scripts: `scripts/`
- Tests: `src/UnitTests/`

Local setup is the README quickstart. Do not invent Compose files, ports or domains. 
