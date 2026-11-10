# REVIEW.md

What a reviewer of an Automaton change looks for. The [PR review skill](.agents/skills/pr-review/SKILL.md), `/code-review`
and any review bot read this file. Keep it short. When a review misses something that later fails on the test server or
in production, add one line here (or a test, or an analyzer rule) instead of a new process step.

## What failure looks like here

- **API (`src/Automaton.Api/`):** someone reads or changes what they should not. For every new or changed endpoint,
  check who gets 200, 403 and 404: the owner, a regular user of another project, a plain Admin, a SysAdmin, an API key,
  nobody signed in.
- **Sign-in and identity:** sign-up re-enabled or the invite flow bypassed; a token, reset link or password logged or
  returned; a rate limit removed.
- **Runner and app runtime:** an app reaches outside its own folder, container or project; a path or shell command
  built from user input; a secret reaching an app that was not granted it; a deploy reported as successful while the
  app is not serving.
- **Migrations:** data dropped or rewritten; a migration that fails on a database with real data in it.
- **Install and update scripts (`docker/install/`, `scripts/`):** a fresh install, or an update from the previous
  release, breaks; a failed update that leaves the server half-updated.
- **Worker and messaging:** a job that runs twice or never; state kept in one process that others need.
- **Studio:** a button that does nothing or reports success on failure; an error the person cannot act on.
- **CLI:** exit code 0 on failure; output an agent cannot parse; a flag or command changed under existing users.
- **Workflows (`.github/workflows/`):** a secret or write permission reachable from pull-request code; a release that
  publishes something that was not built from the release commit.
- **Docs and skills:** they lie (describe behaviour the code does not have), or get longer without saying more.

## Findings

- Every finding has a file, a line and a concrete failure case: this input or state gives this wrong result. Not a
  guess from a name. If you can run it, run it.
- Tag each finding:
  - **Important**: blocks the merge. A wrong result, a security or data-loss problem, or new behaviour without a test.
  - **Nit**: at most three, never blocks.
  - **Pre-existing**: already on `main` before this change. Does not block; file an issue for it.
- For a bug fix, check that the root cause is fixed, not the symptom. When the same mistake exists in other places,
  name them all in one finding.
- Needless code (a helper, option, setting or layer the task does not need) is Important when it adds something public
  (an endpoint, a setting, a CLI flag), otherwise a Nit.
- Drop style, formatting, naming taste and anything CI already reports (analyzers, type-check, CodeQL).
- Start with the counts. Say plainly when nothing needs fixing.

## Skip

Lock files, `*.Designer.cs` and `ApplicationDbContextModelSnapshot.cs` (read the migration itself), `docs/archive/`.
