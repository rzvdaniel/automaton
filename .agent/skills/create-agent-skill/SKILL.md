---
name: create-agent-skill
description: Use when creating, renaming, restructuring, or reviewing Automaton agent skills under .agent/skills.
license: Apache-2.0
compatibility: Repo-local agent skills for coding agents that can read .agent/skills.
metadata:
  author: Automaton
  version: 1.0.0
---

# Create Agent Skill

Use this skill when creating or changing files under:

- `.agent/skills/**`

Automaton skills use a neutral repo-local structure inspired by Anthropic-style skills, but the canonical location for this repository is `.agent/skills/` so the same skills can be used by Claude Code, Codex, Cursor, Copilot, and other agents.

---

## Required structure

Each skill must live in its own kebab-case folder:

```txt
.agent/skills/<skill-name>/SKILL.md
```

Examples:

```txt
.agent/skills/automaton-cli/SKILL.md
.agent/skills/studio-frontend/SKILL.md
.agent/skills/vue-component-splitting/SKILL.md
```

Rules:

- Use kebab-case folder names.
- Use lowercase letters.
- Use hyphens, not spaces or underscores.
- The main file must be named exactly `SKILL.md`.
- Do not use `README.md` inside a skill folder.
- Do not duplicate complete skill content in `AGENTS.md`, `CLAUDE.md`, `.cursor/rules`, or `.github` files.

---

## Optional structure

A skill may include optional supporting folders:

```txt
.agent/skills/<skill-name>/
├── SKILL.md
├── scripts/
├── references/
└── assets/
```

Use optional folders only when they add value. Do not create empty `scripts/`, `references/`, or `assets/` folders just to match a template.

### `scripts/`

Use for helper scripts the agent or developer can run.

Examples:

```txt
.agent/skills/deployment-prod/scripts/check-prod-scripts.sh
.agent/skills/automaton-cli/scripts/verify-cli.sh
```

Rules:

- Scripts must be safe to inspect.
- Scripts must not require secrets unless clearly documented.
- Scripts should be idempotent where practical.
- Scripts should avoid destructive actions by default.
- Scripts should explain their working directory and expected environment.

### `references/`

Use for supporting documentation too large or detailed for `SKILL.md`.

Examples:

```txt
.agent/skills/automaton-cli/references/archive-rules.md
.agent/skills/deployment-prod/references/blue-green-nginx.md
```

Rules:

- Keep `SKILL.md` as the operational entry point.
- Link to references only when needed.
- Do not hide critical rules only in references.
- References can contain longer examples, diagrams, background, or edge cases.

### `assets/`

Use for templates or reusable assets.

Examples:

```txt
.agent/skills/scaffolded-apps/assets/generated-app-AGENTS-template.md
.agent/skills/retro/assets/retro-table-template.md
```

Rules:

- Assets should be safe to copy.
- Assets should not contain secrets.
- Assets should be generic or clearly scoped.

---

## Frontmatter

Every `SKILL.md` should start with YAML frontmatter.

Required:

```yaml
---
name: skill-name
description: Use when...
---
```

Optional:

```yaml
license: Apache-2.0
compatibility: Repo-local skill for agents that can read .agent/skills.
metadata:
  author: Automaton
  version: 1.0.0
```

Keep descriptions action-oriented. They should help an agent decide when to load the skill.

Good:

```yaml
description: Use when changing Automaton CLI deploy, pull, publish, source archives, or automaton.json behavior.
```

Bad:

```yaml
description: A skill about the CLI.
```

---

## Writing rules

A good skill should include:

1. when to use it
2. affected paths
3. hard rules
4. common pitfalls
5. verification commands
6. links to related skills or harness docs

Prefer imperative, concrete rules:

```md
- Do not copy `live/backend/` during fork/remix.
```

Avoid vague advice:

```md
- Be careful with backend files.
```

Keep the skill focused on operational behavior. Do not turn a skill into a full product manual.

---

## When to create a new skill

Create a new skill when:

- the workflow repeats across sessions
- the task has non-obvious rules
- the task has caused mistakes before
- the task spans multiple files or systems
- the task needs dedicated verification
- the topic is too detailed for `AGENTS.md`

Do not create a new skill for:

- one-off facts
- tiny preferences
- generic programming advice
- rules that belong in an existing skill
- project-wide rules that belong in `AGENTS.md`

---

## Naming guidance

Prefer task or domain names over technology-only names.

Good:

```txt
automaton-cli
studio-frontend
studio-backend
app-source-runtime
deployment-prod
runner-debugging
vue-component-splitting
```

Avoid:

```txt
vue
backend
misc
shared
helpers
```

Technology names are acceptable when the skill is truly about that technology-specific pattern, such as `vue-component-splitting`.

---

## Skill size guidance

Keep `SKILL.md` focused.

If it grows too large:

- move examples to `references/`
- move runnable helpers to `scripts/`
- move templates to `assets/`
- split into two skills if there are two distinct workflows

Critical rules must stay in `SKILL.md`; references are for extra detail.

---

## Integration with AGENTS.md

`AGENTS.md` is the repository router.

When adding a broadly useful skill, update the recommended skills list in `AGENTS.md` with a short pointer:

```md
- Skill name: `.agent/skills/skill-name/SKILL.md`
```

Do not copy the full skill into `AGENTS.md`.

Tool-specific files such as `CLAUDE.md`, `.cursor/rules`, or `.github` files should normally point back to `AGENTS.md` and `.agent/` instead of duplicating skill content.

---

## Verification

After creating or changing a skill:

- confirm the folder name is kebab-case
- confirm the main file is exactly `SKILL.md`
- confirm frontmatter has `name` and `description`
- confirm paths and commands are accurate
- confirm no stale `.github/skills` or Claude-specific paths are introduced as canonical
- confirm related skills or harness docs are linked
- confirm `AGENTS.md` only points to the skill and does not duplicate it

For repository-wide verification guidance, read:

```txt
.agent/harness/verification.md
```
