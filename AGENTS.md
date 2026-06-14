# AGENTS.md

This file is the entry point for AI coding agents working on Automaton.

Automaton is an AI-first platform for building, deploying, publishing, and running web apps. Users can create apps locally with an AI coding agent, deploy them with the Automaton CLI, publish them through Automaton Studio, and connect optional Python backends to runners they control.

Use this file as the operational contract. For detailed patterns, read the relevant skill or harness document before changing code.

---

## 1. Core rules

- Prefer small, focused changes over broad refactors.
- Do not edit unrelated code while solving a task.
- Do not rename projects, concepts, routes, or public APIs unless the task explicitly asks for it.
- Do not modify generated/runtime output manually.
- Do not commit secrets, API keys, tokens, `.env` values, local database files, runner state, or build artifacts.
- When fixing a bug, add or update a regression test when practical.
- After changing code, run the smallest useful verification command.
- If verification cannot be run, say exactly why.
- If the task reveals a missing rule, repeated mistake, or unclear workflow, update the relevant skill or harness document.

---

## 2. Agent knowledge layout

Canonical agent material lives under `.agent/`.

- Skills: `.agent/skills/<skill-name>/SKILL.md`
- Harness docs: `.agent/harness/`
- Reusable prompts: `.agent/prompts/`, if present

Before changing code:

1. Identify the affected area.
2. Read the matching skill from `.agent/skills/`.
3. Follow the verification matrix in this file or `.agent/harness/verification.md`, if present.
4. If the task exposes a missing rule or recurring mistake, update the relevant `.agent/` file.

Recommended skills:

- CLI: `.agent/skills/automaton-cli/SKILL.md`
- App source/runtime model: `.agent/skills/app-source-runtime/SKILL.md`
- Production deployment: `.agent/skills/deployment-prod/SKILL.md`
- Scaffolded/generated apps: `.agent/skills/scaffolded-apps/SKILL.md`
- Studio frontend: `.agent/skills/studio-frontend/SKILL.md`
- Studio backend: `.agent/skills/studio-backend/SKILL.md`
- Runner debugging: `.agent/skills/runner-debugging/SKILL.md`

Do not duplicate full skill content into tool-specific files such as `CLAUDE.md`, `.cursor/rules`, or `.github` files. Those files, if present, should point back to `AGENTS.md` and `.agent/`.

---

## 3. Product model

Automaton has four main surfaces:

1. **Website**
   - Public marketing and documentation site.
   - Located in `src/automaton-vue/`.

2. **Studio**
   - Authenticated web UI for apps, publishing, runners, API keys, AI keys, admin features, and account/project/organization management.
   - Frontend located in `src/automaton-studio-vue/`.
   - Backend located in `src/Automaton.Studio.Server/`.

3. **CLI**
   - Local developer tool for creating, pulling, deploying, and publishing Automaton apps.
   - Located in `src/Automaton.Cli/`.

4. **Runner**
   - Executes backend workloads outside the hosted frontend.
   - Located in `src/Automaton.Runner/`.

Automaton apps have an important source/runtime split:

- `source/frontend/` is editable frontend source.
- `source/backend/` is editable backend source.
- `live/frontend/` is deployed public frontend output.
- `live/backend/` is runner-side backend output.
- Studio, CLI pull, remix/fork, snapshots, and marketplace flows should prefer `source/`.
- Public app serving must never expose backend files, secrets, logs, runner state, or unsafe file types.

---

## 4. Repository map

### Website

- `src/automaton-vue/`
- Public marketing site.
- Includes landing pages, pricing, app gallery/public pages, documentation links, and `llms.txt` exposure.

### Studio frontend

- `src/automaton-studio-vue/`
- Vue 3 + TypeScript + Vite + PrimeVue.
- Feature-first structure under `src/features/`.
- Shared components, services, types, and utilities under `src/shared/`.

### Studio backend

- `src/Automaton.Studio.Server/`
- ASP.NET Core 9 REST API + SignalR.
- Controllers, services, EF Core entities/migrations, authentication, runners, apps, publishing, admin.

### CLI

- `src/Automaton.Cli/`
- Commands such as `setup`, `new`, `pull`, `deploy`, and `publish`.
- Must stay friendly to AI-generated local app projects.

### Runner

- `src/Automaton.Runner/`
- Connects to Studio/API and executes backend workloads.
- Runner registration and API keys must avoid leaking secrets.

### Core libraries

- `src/Automaton.Core/`
- `src/Automaton.Steps/`
- `src/Automaton.Core.Scripting/`
- AI provider implementation currently lives under `src/Automaton.Mcp/`; check current project naming and intent before extending it.

### Tests

- `src/UnitTests/`
- Prefer adding tests near the area being changed.

---

## 5. Source/runtime rules for apps

These rules are critical.

### Do

- Use `source/` for editable app content.
- Use `live/` only for deployed/runtime output.
- Keep frontend and backend surfaces separate.
- Preserve `automaton.json` link metadata.
- Default generated local frontend apps to `frontend/` with a build pipeline that outputs to `frontend/dist/`.
- Default generated backend apps to `backend/` when a backend is requested.
- Keep plain root HTML/CSS/JS support only for explicit no-build static apps.

### Do not

- Do not edit `live/frontend/` or `live/backend/` manually.
- Do not copy `live/backend/` during fork/remix.
- Do not include `node_modules`, `dist`, `.vite`, `.venv`, `__pycache__`, `.git`, `.env`, logs, or secrets in source archives.
- Do not expose backend files or unsafe extensions through public app serving.
- Do not assume a backend exists just because a frontend exists.
- Do not pre-create duplicate runner records during hosted runner provisioning.

---

## 6. Verification matrix

Run the smallest relevant verification for the files you changed.

If `.agent/harness/verification.md` exists and is more specific, follow it.

### CLI changes

Affected paths:

- `src/Automaton.Cli/**`
- CLI-related tests under `src/UnitTests/**`

Run:

```bash
dotnet test src/UnitTests/Automaton.Cli.Tests/Automaton.Cli.Tests.csproj
```

If changing archive/deploy/pull behavior, add or update targeted tests for:

- source archive includes expected files
- source archive excludes generated/dependency/secret files
- `automaton.json` link behavior
- clean-folder vs existing-folder pull behavior
- source/runtime separation

---

### Studio backend changes

Affected paths:

- `src/Automaton.Studio.Server/**`
- shared backend models/libraries
- EF Core entities/migrations
- API key, runner, app, publish, auth, admin behavior

Run the relevant backend test project. If unsure, run:

```bash
dotnet test
```

For EF changes:

```bash
dotnet ef migrations list --project src/Automaton.Studio.Server
```

Only add migrations intentionally.

---

### Studio frontend changes

Affected paths:

- `src/automaton-studio-vue/**`

Run:

```bash
cd src/automaton-studio-vue
npm run type-check
npm run build
```

If package dependencies changed, run install first:

```bash
npm install
```

For UI changes, consider desktop and mobile. Check loading, empty, error, and unauthorized states.

---

### Website changes

Affected paths:

- `src/automaton-vue/**`

Run:

```bash
cd src/automaton-vue
npm run build
```

For public pages, check desktop and mobile layout. Avoid changing messaging accidentally.

---

### Runner changes

Affected paths:

- `src/Automaton.Runner/**`
- runner connection, registration, execution, logging, Docker runner config

Run:

```bash
dotnet test
```

Also reason about:

- reconnect behavior
- API key handling
- secret redaction
- runner name matching
- hosted runner compatibility

---

### Production deployment changes

Affected paths:

- `scripts/prod-*.sh`
- `docker-compose.prod.yml`
- nginx/proxy/certbot configuration
- `.prod-runtime` behavior
- production Dockerfiles
- project references or shared source used by production images

Run syntax checks where possible:

```bash
bash -n scripts/prod-redeploy-all.sh
bash -n scripts/prod-redeploy-api.sh
bash -n scripts/prod-redeploy-studio.sh
bash -n scripts/prod-redeploy-website.sh
```

Before changing production deployment logic, identify:

- current live color
- target color
- warmup endpoint
- health check behavior
- proxy reload/cutover step
- rollback path
- what happens when Cloudflare proxy is enabled

Do not remove the previous color before the new color is healthy.

Important:

- A local `.sln` build does not validate Docker build contexts.
- When changing production Dockerfiles, project references, or files copied into production images, ensure the relevant production image validation pipeline covers the change.

---

## 7. Frontend rules

### Architecture

- Use feature-based structure under `src/features/`.
- Keep shared reusable code under `src/shared/`.
- Keep stores lean.
- Keep API access inside service modules.
- Use generated API types when available.

### Components

- Split large conditional blocks into focused components.
- Avoid huge page components with multiple unrelated responsibilities.
- Keep mobile behavior explicit.
- Do not add duplicate global providers such as duplicate toast hosts.
- Prefer accessible labels, semantic buttons, and clear empty states.

### TypeScript

- Prefer typed request/response models.
- Match backend DTO names and shapes.
- Prefer const objects over TypeScript enums when values must match serialized backend strings.
- Do not suppress type errors unless there is a clear reason.

---

## 8. Backend rules

### C# and API

- Use async APIs consistently.
- Respect cancellation tokens where the surrounding code already uses them.
- Keep controllers thin; put business logic in services.
- Return actionable errors.
- Avoid leaking secrets in responses, logs, telemetry, or exceptions.

### Well-known string values

Do not scatter magic strings for fixed modes, states, roles, providers, event types, or action names.

Use `static class` plus `const string` values.

```csharp
public static class ConversationModes
{
    public const string Plan = "plan";
    public const string Agent = "agent";
}
```

Avoid C# enums when the value must be serialized as a stable string and compared to JSON/client values.

### Database

- Do not add columns for every new plan entitlement unless there is a strong query/indexing reason.
- Subscription entitlements should generally extend `PlanCapabilities`.
- Add migrations intentionally.
- Avoid destructive migrations unless explicitly requested.

---

## 9. Authentication, API keys, and secrets

- API keys authenticate runners, web apps, and integrations.
- Expected format: `ak_<scope>_<random>`.
- Never display raw generated API keys after creation unless the current flow is explicitly the one-time reveal.
- Never persist raw API keys when a hash/redacted value is intended.
- Never log raw API keys, access tokens, refresh tokens, provider keys, connection strings, or `.env` values.
- For hosted runner provisioning, store redacted Docker command previews only.

Common scopes:

| Format | Purpose |
|---|---|
| `ak_runner_xxx` | Runner keys |
| `ak_execute_xxx` | External execution/integration keys |
| `ak_readonly_xxx` | Read-only integrations |
| `ak_xxxxxxxxx` | Flexible legacy/general format |

---

## 10. Hosted runners

Hosted runners are managed runner containers, generally one per paid user.

Important areas:

- Entities:
  - `RunnerHost`
  - `HostedRunner`
  - `HostedRunnerDeployment`

- Provisioning service:
  - `src/Automaton.Studio.Server/Services/HostedRunnerProvisioningService.cs`

- Admin API:
  - `src/Automaton.Studio.Server/Controllers/Admin/AdminHostedRunnersController.cs`

- Admin UI:
  - `src/automaton-studio-vue/src/features/admin/views/AdminHostedRunnersView.vue`

Rules:

- Provisioning is config-gated by `HostedRunners:Enabled`.
- Dry-run mode is intentional.
- Plan assignment triggers provisioning.
- Runner registration links back by `AUTOMATON_RUNNER_NAME`.
- Do not pre-create duplicate `Runner` rows.
- Never persist or display raw generated runner API keys.

---

## 11. Publishing, marketplace, and media

- Published apps use public slugs.
- Private deployed app URL:
  - `https://apps.automaton.monster/private/{appId}`
- Published public app URL:
  - `https://apps.automaton.monster/{slug}`

Rules:

- App deploy success should surface the deployed app URL in user-facing output.
- Marketplace media must live under `AppData/media/...`.
- Do not write runtime uploads into `wwwroot`.
- Screenshot galleries can use `WebAppScreenshots`.
- Marketplace cards may fall back to the first screenshot when no thumbnail is set.
- Forks/remixes must preserve attribution and lineage.
- Forked backend apps must warn that execution requires the user’s own runner or a managed runner.

---

## 12. Production deployment rules

The production stack currently uses `docker-compose.prod.yml` and blue/green services for API, Studio, and Website.

Current production model:

- nginx is the public reverse proxy on ports `80/443`.
- Services use blue/green variants:
  - `api-blue` / `api-green`
  - `studio-blue` / `studio-green`
  - `website-blue` / `website-green`
- Live color is tracked under:
  - `.prod-runtime/nginx/state/blue-green-state.env`
- Upstream selectors live under:
  - `.prod-runtime/nginx/upstreams/`

Production docs:

- `docs/prod/production-docker-cheat-sheet.md`
- `docs/prod/nginx-static-blue-green-layout.md`

Rules:

- Keep old containers running until the new color is healthy.
- Warm up new containers before switching traffic.
- Make rollback obvious.
- Do not hide failed warmups.
- Consider Cloudflare proxied mode when debugging.
- Prefer read-only diagnostics before changing production scripts.
- Do not assume a successful local `.sln` build proves the production Dockerfiles still copy the full project graph.

---

## 13. Planning rules

When asked for a plan:

- Focus on implementation order, not human time estimates.
- Avoid fake timelines such as “week 1, week 2.”
- Identify independent tasks that can be done in parallel.
- Include verification.
- Include migration/backward-compatibility concerns when relevant.
- End with open questions only when they block implementation.
- Do not add feature flags, phased rollouts, or heavy ceremonies unless the product is already exposed to real users and the risk justifies them.

---

## 14. Feedback loop

Automaton’s harness must improve when mistakes happen.

After fixing a bug, completing a repeated workflow, or discovering a pitfall, ask:

> Would another agent likely make this same mistake?

If yes, update one of these:

| Situation | Update |
|---|---|
| Missing operational rule | `AGENTS.md` |
| Reusable task workflow | `.agent/skills/<skill-name>/SKILL.md` |
| Detailed implementation guide | `.agent/skills/<skill-name>/SKILL.md` or project docs |
| Architectural decision | `docs/architecture/decisions/` |
| Debugging lesson or pitfall | `.agent/harness/lessons.md` or `FOR-DEVELOPERS.md` |
| Missing verification command | `.agent/harness/verification.md` or this file |

When adding a lesson, keep it short:

```md
## YYYY-MM-DD - Short title

Problem:
What went wrong.

Cause:
Why it happened.

Rule:
What future agents should do differently.

Verification:
Test, command, or manual check that catches it.
```

---

## 15. Final response expectations

When reporting back after code changes:

- Summarize what changed.
- Mention files changed.
- Mention verification run and result.
- If verification was not run, explain why.
- Mention any harness/docs update made.
- Surface deployed app URLs after successful app deploys.
