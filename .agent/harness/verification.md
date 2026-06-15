# Verification

This document defines the verification loop for Automaton agents.

Use it after any code, script, configuration, or documentation change. The goal is not to run every command every time. The goal is to run the smallest verification that can catch likely regressions for the area you changed.

---

## 1. Verification principles

- Run the smallest useful verification for the changed area.
- Prefer targeted tests before broad test suites.
- For bug fixes, add or update a regression test when practical.
- For UI changes, verify both desktop and mobile behavior when the layout can differ.
- For deployment changes, verify syntax, health checks, rollback behavior, and old/new container handoff.
- For generated app or CLI changes, verify source/runtime separation and archive exclusions.
- If a command cannot be run locally, explain why and provide the best manual verification path.
- Do not claim verification passed unless the command actually completed successfully.

---

## 2. Standard verification commands

### Whole repository smoke check

Use this when the change touches multiple .NET projects, shared models, or unknown areas.

```bash
dotnet test
```

Use this carefully: it can be slower than area-specific tests.

---

## 3. CLI verification

Use this when touching:

- `src/Automaton.Cli/**`
- CLI commands such as `new`, `pull`, `deploy`, `publish`, `setup`
- `automaton.json` behavior
- source archive creation
- deploy packaging
- generated local app scaffolding

### Primary command

```bash
dotnet test src/UnitTests/Automaton.Cli.Tests/Automaton.Cli.Tests.csproj
```

### When changing archive/deploy behavior

Add or update targeted tests for:

- `node_modules/` excluded
- `dist/` excluded from source archives
- `.vite/` excluded
- `.venv/` excluded
- `__pycache__/` excluded
- `.git/` excluded
- `.env` excluded
- expected source files included
- nested ignored folders excluded
- large generated/runtime files excluded

Suggested targeted command, if the test names support it:

```bash
dotnet test src/UnitTests/Automaton.Cli.Tests/Automaton.Cli.Tests.csproj --filter Zip
```

or:

```bash
dotnet test src/UnitTests/Automaton.Cli.Tests/Automaton.Cli.Tests.csproj --filter Archive
```

### When changing `automaton pull`

Verify:

- linked project pull still works
- pull into a new folder behaves intentionally
- existing local files are not overwritten silently
- `automaton.json` is created or preserved correctly
- source files are preferred over runtime files
- `live/backend/`, secrets, logs, and runner state are never pulled as editable source

### When changing `automaton deploy`

Verify:

- frontend build output is deployed from the configured output folder
- backend source is copied/deployed only through the intended backend path
- deploy excludes dependencies, generated files, local caches, and secrets
- user-facing output includes the deployed app URL when deploy succeeds

---

## 4. Studio backend verification

Use this when touching:

- `src/Automaton.Studio.Server/**`
- controllers
- services
- entities
- migrations
- authentication
- API keys
- runners
- hosted runners
- apps/publishing/forking/remix
- admin features

### Primary command

Run the most specific backend test project if known. Otherwise:

```bash
dotnet test
```

### For EF Core changes

Check migrations intentionally:

```bash
dotnet ef migrations list --project src/Automaton.Studio.Server
```

When a schema change is needed, create the migration with EF tooling rather than hand-writing files:

```bash
dotnet ef migrations add <MigrationName> --project src/Automaton.Studio.Server
```

When adding a migration, verify:

- the migration contains only intended schema changes
- destructive operations are expected and justified
- default values/backfill behavior are safe
- production configuration is not required for local migration inspection

### For API changes

Verify:

- request/response DTOs remain compatible or the breaking change is intentional
- frontend generated types are updated if required
- unauthorized/forbidden/not-found cases are handled
- errors do not leak secrets or internal details
- cancellation tokens are preserved where already used

### For API key or auth changes

Verify:

- raw keys are shown only during intentional one-time reveal flows
- raw keys are not persisted when hashes/redacted values are expected
- logs do not include API keys, provider keys, tokens, or connection strings
- runner-scoped keys cannot be used for unrelated scopes

---

## 5. Studio frontend verification

Use this when touching:

- `src/automaton-studio-vue/**`
- Vue components
- Pinia stores
- services/API client code
- routing
- app editor
- app details/gallery/publishing screens
- admin screens

### Primary commands

```bash
cd src/automaton-studio-vue
npm run type-check
npm run build
```

If dependencies changed:

```bash
cd src/automaton-studio-vue
npm install
npm run type-check
npm run build
```

### UI checklist

For UI changes, verify or reason through:

- desktop layout
- mobile layout
- loading state
- empty state
- error state
- unauthorized/forbidden state, if applicable
- long text/names
- missing images/media
- keyboard and basic accessibility behavior for dialogs/buttons

### Components

Check that the change does not introduce:

- duplicate global providers such as duplicate toast hosts
- giant conditional templates that should be split
- business logic hidden in page templates
- direct API calls scattered outside feature services
- hardcoded serialized values that should use shared constants

### API type generation

If backend DTOs or OpenAPI output changed, run the project’s type generation command if available:

```bash
cd src/automaton-studio-vue
npm run generate:types
```

Then rerun:

```bash
npm run type-check
npm run build
```

---

## 6. Website verification

Use this when touching:

- `src/automaton-vue/**`
- public landing pages
- pricing
- apps/public app gallery pages
- docs pages
- website assets
- public `llms.txt` exposure

### Primary command

```bash
cd src/automaton-vue
npm run build
```

### UI/content checklist

Verify or reason through:

- desktop layout
- mobile layout
- header navigation
- footer links
- CTA links
- public app links
- pricing wording
- screenshots/images loading
- no accidental change to product positioning

---

## 7. Runner verification

Use this when touching:

- `src/Automaton.Runner/**`
- runner registration
- SignalR connection behavior
- runner execution
- backend app deployment to runner
- runner Docker files/configuration

### Primary command

```bash
dotnet test
```

### Runner checklist

Verify or reason through:

- reconnect behavior
- runner name matching
- API key scope and redaction
- logs do not leak secrets
- hosted runner compatibility
- local runner compatibility
- backend app paths are isolated per app/user
- failed execution returns useful diagnostics

---

## 8. Hosted runner verification

Use this when touching:

- `HostedRunnerProvisioningService`
- `RunnerHost`
- `HostedRunner`
- `HostedRunnerDeployment`
- hosted runner admin API
- hosted runner admin UI
- plan assignment/provisioning behavior

### Primary command

```bash
dotnet test
```

If UI changed too:

```bash
cd src/automaton-studio-vue
npm run type-check
npm run build
```

### Hosted runner checklist

Verify:

- provisioning remains config-gated by `HostedRunners:Enabled`
- dry-run mode remains safe
- plan assignment triggers the intended provisioning path
- runner registration links by `AUTOMATON_RUNNER_NAME`
- duplicate `Runner` rows are not created
- raw runner API keys are never persisted or displayed after one-time reveal
- redacted Docker command previews remain redacted

---

## 9. App source/runtime verification

Use this when touching:

- app deploy logic
- fork/remix logic
- app archive import/export
- marketplace copying
- Studio editor source handling
- public runtime file serving
- backend server tab behavior

### Checklist

Verify:

- `source/frontend/` remains editable frontend source
- `source/backend/` remains editable backend source
- `live/frontend/` remains public deployed frontend output
- `live/backend/` remains runner-side backend output
- Studio editing uses `source/`
- CLI pull uses `source/` when available
- fork/remix uses portable source
- fork/remix never copies `live/backend/`
- secrets, logs, runner state, and user data are never copied
- public runtime serving uses allowlists or safe file rules
- sensitive extensions return 404 or equivalent safe result

---

## 10. Production deployment verification

Use this when touching:

- `scripts/prod-*.sh`
- `docker-compose.prod.yml`
- nginx configuration
- proxy configuration
- certbot scripts
- blue/green deployment state
- `.prod-runtime/**` behavior
- production Dockerfiles
- project references or shared source used by production images

### Syntax checks

Run relevant syntax checks:

```bash
bash -n scripts/prod-redeploy-all.sh
bash -n scripts/prod-redeploy-api.sh
bash -n scripts/prod-redeploy-studio.sh
bash -n scripts/prod-redeploy-website.sh
bash -n scripts/prod-verify.sh
bash -n scripts/prod-reset-blue-green.sh
```

Only run commands for files that exist.

### Deployment checklist

Before changing production deployment logic, identify:

- current live color
- target color
- services affected
- warmup endpoint
- health check command
- proxy reload/cutover step
- rollback path
- whether old containers remain alive until new containers are healthy
- whether Cloudflare proxied mode changes behavior

### Critical rules

- Do not remove the previous color before the new color is healthy.
- Do not hide failed warmups with broad `|| true` unless the failure is intentionally non-blocking and documented.
- Do not reload proxy config before upstream files are valid.
- Do not make rollback harder.
- Prefer read-only diagnostics before production changes.
- A local `.sln` build does not validate Docker build contexts or `COPY` coverage.
- When changing production Dockerfiles, project references, or files copied into production images, validate the relevant production image build path in CI.

---

## 11. Documentation and harness verification

Use this when touching:

- `AGENTS.md`
- `.agent/**`
- `FOR-DEVELOPERS.md`
- architecture docs
- skills
- prompts

### Checklist

Verify:

- there is one canonical source of truth
- tool-specific files are pointers, not full duplicated instruction sets
- paths mentioned in docs exist or are intentionally proposed
- commands are copy-pasteable from repo root unless documented otherwise
- old `.github/skills` references are not reintroduced as canonical
- the feedback loop points to `.agent/skills` and `.agent/harness`

For Markdown-only changes, no build is usually required unless the docs are rendered by the website.

If docs are rendered by the website, run:

```bash
cd src/automaton-vue
npm run build
```

---

## 12. Suggested future verification scripts

If scripts are added, prefer this interface:

```bash
scripts/verify.sh cli
scripts/verify.sh studio-backend
scripts/verify.sh studio-frontend
scripts/verify.sh website
scripts/verify.sh runner
scripts/verify.sh prod-scripts
scripts/verify.sh all
```

And Windows equivalents:

```powershell
scripts/verify.ps1 cli
scripts/verify.ps1 studio-backend
scripts/verify.ps1 studio-frontend
scripts/verify.ps1 website
scripts/verify.ps1 runner
scripts/verify.ps1 prod-scripts
scripts/verify.ps1 all
```

Once these scripts exist, agents should prefer them over inventing commands.

---

## 13. Final report format

When reporting back after a change, include:

```md
## Summary
- What changed.

## Files changed
- `path/to/file`

## Verification
- Command run: `...`
- Result: passed/failed/not run

## Notes
- Any limitations, follow-up, or harness update.
```

If verification was not run, use:

```md
## Verification
Not run: <specific reason>.
Recommended check: `<command>`.
```

Do not imply the code is verified when only static reasoning was performed.
