# Orchestrator configuration (template)

Copy this file to `.agents/orchestrator.config.md` and fill every section. The orchestrator skill reads it first. Do not put
secrets here: name where each secret lives.

## Project
- Name, repository (`owner/name`), the owner's name and how to reach them.
- Default branch and its protection (who may push directly, who needs a pull request).

## Agents
For each agent (a headless agent on a server, a Desktop session, subagents):
- Name, where it runs, which account or plan it uses (and its usage limits).
- **How to start a task** (exact command), where its log is, how to see whether it is alive and when it last worked.
- Model and effort settings to use; what kind of work it takes (its lane); what it must never touch.
- How many agents may run at once, and after how many quiet minutes an agent counts as stalled (and the command that watches for it).
- How tasks are queued behind a running one.

## Environments
For each environment (test, production, temporary servers):
- Name, purpose, URLs (app, API, health check), how to reach it (the exact ssh alias or CLI profile), which user.
- Where credentials live (a path or a vault, never the values) and the rules for using them.
- What may be done there without asking and what needs the owner.

## Boards and issues
- Issue tracker, boards or projects (URL, purpose, fields and their exact option names), parent issues, labels in use.
- Where each kind of issue is filed; how items are added and their status/priority set.

## Release and production
- The release command; how to check it; how long it takes; the test-environment update step.
- The production update steps and verification (health URLs, what to check).
- Steps for anything deployed separately from the main release (website, apps, runners).
- Pre-approvals from the owner (for example "verified releases go to production without asking") and the things that
  always need a question first.

## Standing decisions
A dated list of the owner's rules (process, quality bar, what is never done). Add to it whenever the owner decides something
that should last.

## Local quirks and traps
Things specific to this project, machine or tooling that cost time (shell wrappers, API limits, setup that must not be
changed, naming of options, known flaky steps).
