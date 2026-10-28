---
title: Properties
order: 20
---

# AI Guidance Properties

## Document Types

### Base Prompt

The Base Prompt defines the overall AI contract.

Characteristics:

- Always injected
- Small
- Stable
- Not discoverable by the model

Typical examples:

- Automaton App Contract

Recommended settings:

```text
Surface = Shared
Type = BasePrompt
Required = Yes
Discoverable = No
```

### Guidance

Guidance documents teach the AI about a specific topic.

Examples:

- Studio Editor
- Actions
- MCP

Guidance may be:

- Required
- Optional
- Discoverable
- Exported to projects

## Property Reference

### Surface

Defines the primary audience.

Examples:

```text
Shared
Studio
CLI
MCP
```

### Type

Defines the document's role.

Values:

```text
BasePrompt
Guidance
```

Use BasePrompt sparingly.

Most documents should be Guidance.

### Required

Required documents are automatically injected into the prompt.

Optional documents are only loaded when needed.

### Discoverable

When enabled, the AI may discover the document through:

```text
list_automaton_guidance()
get_automaton_guidance()
```

Base Prompts are normally not discoverable.

### Output Path

If specified, the document is eligible for CLI export only when its Surface is
`cli`. The normal built-in CLI export contains only `AGENTS.md` and the local
Automaton app skill.

Examples:

```text
AGENTS.md
.automaton/agents/skills/automaton-app/SKILL.md
```

Documents without an Output Path are runtime-only.

### Sort Order

Controls ordering.

Typical values:

```text
0      Base Prompt
10     Required Guidance
100+   Optional Guidance
```

Use only when ordering matters.
