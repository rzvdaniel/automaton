---
title: Overview
order: 0
---

# AI Guidance Overview

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

## Surfaces

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
