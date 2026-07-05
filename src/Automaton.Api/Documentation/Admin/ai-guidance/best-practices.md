---
title: Best Practices
order: 40
---

# AI Guidance Best Practices

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
