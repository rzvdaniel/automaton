# AI Guidance Handbook

> The AI Guidance Handbook explains how Automaton's AI harness works, how guidance documents are consumed by Studio, CLI and MCP, and how to design new guidance documents correctly.

---

# Overview

Automaton uses AI Guidance Documents to control how AI behaves across the platform.

The architecture is intentionally simple:

```text
Embedded Assets
        |
        | First installation only
        v
Seed Database
        |
        v
+--------------------------+
| AI Guidance Database     |
+--------------------------+
        |
        |-- Studio AI
        |-- CLI Export
        |-- MCP Tools
        \-- Admin UI
```

The guiding principle is:

> The database is the single source of truth.

Embedded markdown files are only used to populate an empty database during installation.

---

# Architecture

Every AI request is built from a combination of guidance documents.

For Studio AI Chat, the prompt is assembled like this:

```text
Shared Base Prompt
        |
        v
Required Studio Guidance
        |
        v
Optional Guidance
(selected automatically)
        |
        v
Conversation
        |
        v
LLM
```

The AI never receives every guidance document.

Only the documents relevant to the current request are included.

---

# Document Types

## Base Prompt

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

---

## Guidance

Guidance documents teach the AI about a specific topic.

Examples:

- Studio Editor
- Backend Controllers
- Frontend Structure
- CLI Deploy
- MCP
- Examples

Guidance may be:

- Required
- Optional
- Discoverable
- Exported to projects

Recommended settings depend on the scenario.

---

# Surfaces

Surface identifies the primary consumer of a document.

| Surface | Studio | CLI | MCP |
|---|:---:|:---:|:---:|
| Shared | Yes | Yes | Yes |
| Studio | Yes |  |  |
| CLI |  | Yes |  |
| MCP |  |  | Yes |

Shared documents are reusable across multiple consumers.

Surface does not determine loading by itself.

Loading also depends on:

- Required
- Discoverable
- Output Path

---

# Runtime Loading

## Studio

Studio automatically loads:

- Shared Base Prompt
- Required Studio Guidance

Optional guidance is selected dynamically when relevant.

Example:

```text
User:

Create a new ASP.NET controller
```

The AI may additionally load:

- Backend Controllers
- Coding Style
- REST API Guidance

---

## CLI

The CLI does not build prompts.

Instead it requests the current guidance pack from the API.

```text
CLI
|
v
GET /api/ai-guidance/export
|
v
API builds project files
|
v
CLI writes them locally
```

---

## MCP

The MCP guidance tool exposes only discoverable guidance.

The AI can:

```text
list_automaton_guidance()
```

```text
get_automaton_guidance(key)
```

Base Prompts are never listed.

---

# CLI Export

The export endpoint generates the project AI assets.

Examples:

```text
AGENTS.md

.automaton/
    agents/
        skills/
            automaton-app/
                SKILL.md
        references/
            *.md
```

Future project files may also be exported from the database.

The CLI simply writes the exported files.

---

# Document Properties

## Surface

Defines the primary audience.

Examples:

```text
Shared
Studio
CLI
MCP
```

## Type

Defines the document's role.

Values:

```text
BasePrompt
Guidance
```

Use BasePrompt sparingly.

Most documents should be Guidance.

## Required

Required documents are automatically injected into the prompt.

Optional documents are only loaded when needed.

## Discoverable

When enabled, the AI may discover the document through:

```text
list_automaton_guidance()
get_automaton_guidance()
```

Base Prompts are normally not discoverable.

## Output Path

If specified, the document is exported into generated projects.

Examples:

```text
AGENTS.md
.automaton/agents/references/llms/backend.md
```

Documents without an Output Path are runtime-only.

## Sort Order

Controls ordering.

Typical values:

```text
0      Base Prompt
10     Required Guidance
100+   Optional Guidance
```

Use only when ordering matters.

---

# How To Configure A Guide

Use these recipes as starting points when creating or editing guidance documents.

## Shared Base Prompt

The foundation of the AI harness.

| Property | Value |
|---|---|
| Surface | `shared` |
| Type | `BasePrompt` |
| Required | Yes |
| Discoverable | No |
| Output Path | empty |

Use for:

- AI contract
- Core behavior
- Global instructions

## Studio Required Guide

Automatically injected into every Studio AI conversation.

| Property | Value |
|---|---|
| Surface | `studio` |
| Type | `Guidance` |
| Required | Yes |
| Discoverable | `true` or `false` |
| Output Path | empty |

Use for:

- Studio editing rules
- File editing behavior
- Studio-specific policies

## Studio Optional Guide

Loaded only when relevant.

| Property | Value |
|---|---|
| Surface | `studio` or `shared` |
| Type | `Guidance` |
| Required | No |
| Discoverable | Yes |
| Output Path | empty |

Use for:

- Framework guidance
- Coding examples
- Specialized knowledge

## CLI Exported Guide

Exported into generated projects as a reference file.

| Property | Value |
|---|---|
| Surface | `cli` or `shared` |
| Type | `Guidance` |
| Required | No |
| Discoverable | Yes |
| Output Path | `.automaton/agents/references/llms/{key}.md` |

Use for:

- Deployment guidance
- CLI references
- Local AI knowledge

## CLI Project File

Written directly into generated projects.

| Property | Value |
|---|---|
| Surface | `cli` |
| Type | `Guidance` |
| Required | No |
| Discoverable | No |
| Output Path | `AGENTS.md` or another project path |

Use for:

- `AGENTS.md`
- Skills
- Project-level AI helper files

---

# Best Practices

## Keep Base Prompts small

The Base Prompt should define the contract.

Do not put detailed implementation guidance here.

## Prefer Guidance

When in doubt, create a Guidance document.

Only create a Base Prompt if it truly belongs to the AI harness foundation.

## One topic per document

Good:

- Studio Editor
- Backend Controllers
- Vue Components

Avoid:

- Everything About Studio

## Prefer optional guidance

Only mark a document as Required if every request needs it.

Otherwise let the AI load it when appropriate.

## Keep documents reusable

Shared guidance is usually preferable to duplicating similar documents for Studio and CLI.
