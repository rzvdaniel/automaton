---
title: Recipes
order: 30
---

# AI Guidance Recipes

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
