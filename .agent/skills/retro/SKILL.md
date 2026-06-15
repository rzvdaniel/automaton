---
name: retro
description: Retrospective skill for durable harness improvements. Use at the end of substantial sessions, and automatically consider it when a session creates a reusable rule, new skill, architecture decision, or safety guardrail.
---

# Session Retrospective

Use this skill to turn collaboration friction into durable improvements for future AI agents working on Automaton.

The goal is to improve the harness over time: `AGENTS.md`, `.agent/skills/`, `.agent/harness/`, and related project documentation should get better when mistakes reveal missing rules.

Do not run a retro for every small exchange. Use it when there was enough substance to learn from.
Do not interrupt active implementation just to run a retro. If a retro is useful, mention it near the end of the task or when the user asks about documentation, lessons, or what remains.

---

## Threshold check

Before starting, assess whether a retro is useful.

Skip the retro unless the user insists when:

- the task had fewer than about five substantive exchanges
- there were no corrections, reversals, repeated misunderstandings, or tool errors
- the conversation only involved a simple answer or one-off file generation

Proceed when:

- the user explicitly asks for a retro
- the user corrected or redirected the agent multiple times
- the agent used the wrong files, commands, paths, tools, or assumptions
- a bug or workflow gap revealed a missing durable rule
- the session produced a reusable workflow that should become a skill or checklist

## Auto-trigger signals

Automatically consider this skill when:

- a new skill is proposed or created
- the user asks where a rule should live
- the user asks where a rule, convention, or decision should be documented
- the user asks whether something should be documented
- the session establishes a reusable coding convention
- the session creates or clarifies an architecture pattern
- a refactoring touches several services or files and reveals a reusable rule
- a safety rule emerges, especially around secrets, auth, deploys, logging, or data loss

Use these as prompts to consider a retro, not as a requirement to stop implementation immediately.
If the work is still active, defer the retro until a natural pause or the end of the task.

## End-of-session prompt

When a retro appears useful but has not been explicitly requested, briefly ask:

```txt
Would you like a quick retro to capture the lessons from this session?
```

Use this near the end of the task or at a natural pause.
Do not interrupt active implementation just to ask.

---

## Phase 1: Detect correction signals

Review the conversation and look for signals worth capturing.

### High-confidence signals

Capture these when they reveal a reusable lesson:

- “No”, “that’s wrong”, “I said...”, “don’t do that”, “not like that”
- the user repeats an instruction the agent missed
- the user undoes or reverts agent work
- the user says “use X, not Y”
- the user says “always do X” or “never do Y”
- the user points out stale architecture, stale docs, or obsolete tooling

### Medium-confidence signals

Capture only if they reveal an undocumented preference or rule:

- “Perfect”, “exactly”, “that’s right”, “yes, like that”
- the user validates an approach that should become the default
- the user confirms a naming/location convention

### Low-confidence signals

Capture only if repeated or clearly useful:

- the user manually performs work the agent should have done
- the agent wastes effort by looking in the wrong place
- the agent asks for information that was already available
- too much back-and-forth was needed for a predictable task

### Tool-error signals

Capture concrete tool mistakes:

- wrong CLI subcommand, flag, or argument
- wrong file path or stale folder structure
- wrong GitHub/API/tool parameter shape
- wrong order of tool calls
- repeated retries of the same failing invocation
- failure to verify a file exists before referencing it

For each tool error, record the wrong invocation and the correct one if known.

Skip:

- simple typos
- one-off misunderstandings
- preferences already documented in `AGENTS.md`, `.agent/skills/`, or `.agent/harness/`
- generic best practices that do not need Automaton-specific rules

---

## Phase 2: Classify improvements

Classify each useful signal.

| Category | Meaning | Example |
|---|---|---|
| Behavioural | How the agent should behave differently | “Read the current file before proposing a replacement.” |
| Guardrail | A hard rule to prevent a repeated failure | “Never edit `live/backend/` manually.” |
| Verification | A test, command, or checklist that should catch the issue | “Run CLI archive tests after changing deploy packaging.” |
| Skill | A reusable task workflow should become or update a skill | “Production deploy debugging needs a dedicated skill.” |
| Architecture decision | A durable product/technical decision should be captured | “Production uses nginx blue/green upstream switching.” |
| Tech debt | A code or docs cleanup that should happen later | “Rename misleading project once dependent references are mapped.” |
| Backlog | A useful idea outside the current task | “Add `automaton doctor app`.” |

Only Behavioural, Guardrail, Verification, Skill, and Architecture decision items should usually update harness files.

Tech debt and Backlog items should be reported separately unless the user explicitly asks to create issues or update docs.

---

## Phase 3: Map to destinations

Store lessons in the most specific durable place.

| Pattern | Destination |
|---|---|
| Applies to one existing skill | `.agent/skills/<skill-name>/SKILL.md` |
| Defines a new reusable workflow | new `.agent/skills/<skill-name>/SKILL.md` |
| Applies to verification commands/checklists | `.agent/harness/verification.md` |
| Applies to repeated mistakes or pitfalls | `.agent/harness/lessons.md` |
| Applies to final response/review behavior | `.agent/harness/review-checklist.md` |
| Applies to the whole repository | `AGENTS.md` |
| Explains a durable architecture/product decision | `docs/architecture/decisions/` |
| Explains project background for humans | `FOR-DEVELOPERS.md` or normal project docs |
| Applies only to a specific tool integration | that tool’s thin pointer file, only if present |

Specificity rule: write the lesson to the narrowest place that future agents will actually read.

Examples:

- A CLI packaging rule belongs in `.agent/skills/automaton-cli/SKILL.md`, not in `AGENTS.md`.
- A source/runtime rule used across Studio, CLI, and fork/remix belongs in `.agent/skills/app-source-runtime/SKILL.md` or `AGENTS.md` if truly universal.
- A command to run after frontend changes belongs in `.agent/harness/verification.md`.
- A one-time bug story belongs in `.agent/harness/lessons.md`, not in every skill.

Avoid adding full instruction content to `CLAUDE.md`, `.cursor/rules`, `.github`, or other tool-specific files. Those files should usually remain thin pointers to `AGENTS.md` and `.agent/`.

---

## Phase 4: Present findings for approval

Before editing files, present findings in a compact table.

| # | Signal | Category | Proposed durable rule/change | Destination |
|---|---|---|---|---|
| 1 | Agent looked for old nested skill paths | Guardrail | Skills live directly under `.agent/skills/<skill-name>/SKILL.md`; do not assume category subfolders. | `AGENTS.md` or `.agent/harness/lessons.md` |
| 2 | Deploy script change needed shell syntax validation | Verification | Run `bash -n scripts/prod-*.sh` after production script edits. | `.agent/harness/verification.md` |
| 3 | A reusable CLI archive workflow emerged | Skill | Create/update an `automaton-cli` skill with archive exclusions and test commands. | `.agent/skills/automaton-cli/SKILL.md` |

Ask for approval before applying changes when the retro would modify files.

The user may reject, modify, or re-categorize items.

---

## Phase 5: Write durable rules

When applying approved changes:

1. Read the target file first.
2. Check that the rule does not already exist in the target or a higher-priority location.
3. Add the rule to the most relevant section.
4. Write concise imperative guidance.
5. Avoid narrative session history in rule files.
6. Keep files focused. If a file is getting too large, suggest splitting details into `.agent/skills/`, `.agent/harness/`, or project docs.
7. Prefer updating existing skills/checklists over creating new ones unless the workflow is clearly distinct.

Golden rule: do not store user input verbatim. Synthesize it into a reusable instruction.

Bad:

```md
User said there are no Traefik mentions anymore.
```

Good:

```md
Production deployment docs should describe the current nginx blue/green model only. Do not reintroduce obsolete infrastructure references.
```

Bad:

```md
The agent failed because it searched `.github/skills`.
```

Good:

```md
Canonical skills live in `.agent/skills/<skill-name>/SKILL.md`; do not use `.github/skills` as the default skill location.
```

---

## Phase 6: Verify harness edits

After modifying harness files:

- Re-read each changed file.
- Confirm paths are accurate.
- Confirm there are no stale references to old skill locations.
- Confirm tool-specific files remain pointers rather than duplicated sources of truth.
- Confirm the new rule does not contradict `AGENTS.md`.
- If verification commands changed, make sure they are copy-pasteable from the documented working directory.

For Markdown-only harness edits, no build is usually required unless those docs are rendered by the website.

If docs are rendered by the website, run the website build command from `.agent/harness/verification.md`.

---

## What not to capture

Do not capture:

- things that already worked
- session-specific facts that will not help future agents
- private user details unrelated to the repository workflow
- speculative improvements not backed by friction
- one-time typo fixes
- generic software advice
- duplicate rules already covered elsewhere
- raw secrets, tokens, API keys, personal data, or private operational details

---

## Output format

When presenting a retro, use:

```md
## Retro findings

| # | Signal | Category | Proposed durable rule/change | Destination |
|---|---|---|---|---|
| 1 | ... | ... | ... | ... |

## Suggested file updates

- `path/to/file`: short description

## Items not written to harness

- Tech debt or backlog items, if any.
```

When updates are approved and applied, finish with:

```md
## Updated

- `path/to/file`: what changed

## Verification

- Re-read changed harness files.
- No duplicate or contradictory rules found.
```

---

## Integration

This skill is part of the neutral Automaton agent harness.

Related files:

- `AGENTS.md` — repository-level agent contract
- `.agent/skills/<skill-name>/SKILL.md` — task-specific skills
- `.agent/harness/verification.md` — verification loop
- `.agent/harness/lessons.md` — durable lessons and pitfalls, if present
- `.agent/harness/review-checklist.md` — final self-review checklist, if present
- `.agent/prompts/` — reusable prompts, if present
