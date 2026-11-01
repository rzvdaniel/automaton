# AGENTS.md

This file is the platform contract for coding agents working in the Automaton repository.

Automaton is an AI-first platform for building, deploying, publishing, and running web apps. Keep changes focused, preserve existing behavior outside the task, and add regression coverage when practical.

This is not the `AGENTS.md` the CLI generates inside an app folder. For hosted-app work, follow that app's generated guidance.

## Repository skills

Read the matching skill before changing the related area:

| If the task touches… | Read |
| --- | --- |
| Method complexity, class coupling, service responsibilities, or type ownership | [Maintainability](.agents/skills/maintainability/SKILL.md) |
| App source/runtime, `source/` / `live/`, serving, archives, forks, or remixes | [Generic runtime](.agents/skills/generic-runtime/SKILL.md) |
| Runtime placement, Infrastructure Runners, deployments, workers/messaging, persistent storage, scheduling, runtime routing, distributed state, or infrastructure providers | [Distributed platform architecture](.agents/skills/distributed-platform-architecture/SKILL.md) |
| Production scripts, Compose, Traefik, certificates, health checks, blue-green, or rollback | [Production deployment](.agents/skills/production-deployment/SKILL.md) |
| Fresh production installation, setup configuration, secrets, or bootstrap | [Production installation setup](.agents/skills/production-installation-setup/SKILL.md) |
| Docker disk space on a production host | [Production Docker maintenance](.agents/skills/production-docker-maintenance/SKILL.md) |
| Administrator-visible Studio or API behavior and its documentation | [Admin docs](.agents/skills/admin-docs/SKILL.md) |
| Roadmaps, architecture guides, or other repository documentation | [Documentation maintenance](.agents/skills/documentation-maintenance/SKILL.md) |
| User-facing Studio UI, theme tokens, or visual verification | [Studio design](.agents/skills/studio-design/SKILL.md) |
| Delivering an issue or PR end to end: review, merge, release, test-server upgrade, and verification as real users | [Full delivery](.agents/skills/full-delivery/SKILL.md) |

The skills listed above are the repository contract. Other folders under `.agents/skills/` are developer-local. Do not add, expand, or commit local skills unless the task names that work.

## Browser Automation

Use `agent-browser` for web automation. Run `agent-browser --help` for all commands.

Core workflow:

1. `agent-browser open <url>` - Navigate to page
2. `agent-browser snapshot -i` - Get interactive elements with refs (@e1, @e2)
3. `agent-browser click @e1` / `fill @e2 "text"` - Interact using refs
4. Re-snapshot after page changes

## Agent orchestration

Repository coding agents follow this file and the matching repository skills directly. No separate local orchestrator is required. Do not add manager/developer/reviewer role prompts, agent loops, orchestration state, metrics harnesses, or equivalent machinery under `.agents/` or `AgentRuns/`.

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

## Keep implementation simple

- Before substantial work, briefly state the required outcome and what completes the task. Reuse the existing task or roadmap; do not create another plan or report by default.
- Choose the smallest complete change through existing code paths. Add an abstraction, interface, service, configuration option, or lifecycle state only when a concrete requirement or actual consumers justify it.
- Do not build speculative provider support, frameworks, compatibility layers, or future milestones. Prioritize a working user flow over disconnected preparation slices.
- Address concrete security and correctness failures with the simplest effective boundary. Preserve isolation, authorization, data integrity, and recovery guarantees; simplicity does not justify weakening them.
- Keep validation proportional to the change: run required builds and focused tests of changed behavior and identified failure cases. Broaden or repeat checks only for a specific unresolved concern. Record unavailable checks and agreed deferred acceptance accurately without creating additional gates.
- Before handoff, review the diff for unnecessary helpers, configuration, documentation, and indirection introduced by the change, and remove what the task does not need.
- If scope grows substantially, explain the concrete reason and simplest viable approach. Do not silently turn a bounded fix into a redesign or add approval stages for routine implementation choices.
- When writing implementation prompts, describe the outcome, constraints, concrete failure, and acceptance evidence. Let the implementing agent choose the smallest safe solution; prescribe architecture only when an established contract or verified need requires it. Distinguish required fixes from optional improvements in reviews.

## Maintainability guardrails

- Before extending a service, identify the feature and responsibility that own the new behavior. Keep application services focused on coordinating use cases; place cohesive business decisions and invariants in their owning object or policy.
- Do not add unrelated DTOs, options, or general utilities to a service file or a shared `Services` folder. Keep supporting types with the feature that owns them; share a helper only when real consumers need the same semantics.
- Prefer composition and clear contracts. Add an interface for an actual boundary or variation point, not automatically for every class. Pure functions and data-only transport models are appropriate when they fit.
- Treat .NET analyzer findings as review input, including CA1502 complexity and CA1501 inheritance. CA1502 is a build error and methods should stay below cyclomatic complexity 15. Keep classes cohesive around their responsibilities; split them when responsibilities belong to distinct concerns, not just to reduce class size or type coupling. Do not hide complexity by splitting one algorithm into meaningless helpers.
- Triage analyzer output in this order: build errors; security and correctness warnings affecting changed behavior; complexity and coupling in changed symbols; other warnings. Review high-risk findings even when their rule is not yet a build error. The rule choices are documented in `docs/architecture/analyzer-priority-baseline.md`.
- When changing a large service, check its responsibilities and dependency list, then make the smallest cohesive improvement within the task's scope. Preserve behavior, public contracts, cancellation, query semantics, transactions, and concurrency guarantees.
- For C# changes, build the affected project with `dotnet build <affected-project.csproj> -c Debug --no-incremental -v minimal` before editing and save its analyzer diagnostics for the files and symbols you will touch. Run it again after editing and resolve the relevant build errors. Before handoff, run `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal`; this solution includes every tracked C# project under `src/`, including tests and fixtures. Incremental builds may omit diagnostics from skipped projects, so use `--no-incremental` when checking analyzer output.
- Resolve analyzer errors in changed code. The all-project build currently reports historical complexity debt too; make progress on those findings in scoped, behavior-preserving batches. If unrelated historical findings block a focused build, identify them explicitly rather than suppressing them. If a justified exception remains, explain the finding, reason, and scope in the handoff.
- Run affected tests and review the diff after the analyzer check. Report the before/after findings relevant to the change and any unavailable check. Do not weaken analyzer settings, change thresholds, or add suppressions merely to complete a task.

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

Architecture guides under `docs/architecture/` describe the implemented system; keep the relevant guide up to date in the same change whenever architecture, state ownership, message flow, runtime boundaries, or operational guarantees change.

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

For production operations, follow the preflight, release, update, and verification workflow in the production deployment skill. Do not deploy or change production data without an explicit request.
