---
title: Build with AI tools
description: Use an AI coding agent with the Automaton CLI and generated local guidance.
---

# Build with AI

Automaton gives a local coding agent two files. `AGENTS.md` points to the
canonical app skill at `.automaton/agents/skills/automaton-app/SKILL.md`.

Give the agent a plain application request. It should inspect `automaton.json`,
choose its own technology and source layout, implement the app, configure the
generic contract, validate, deploy, wait for `Succeeded`, and verify the URL.

The short prompt is:

```text
Build [APP IDEA] as an Automaton app and deploy it.
```

The agent should run `automaton new` for a new app or `automaton link <app-id>`
for an existing app. It should run `automaton validate` before
`automaton deploy`. Do not publish without approval.

Do not paste an API key into the AI conversation. Use `automaton login` or the
process environment for unattended work.
