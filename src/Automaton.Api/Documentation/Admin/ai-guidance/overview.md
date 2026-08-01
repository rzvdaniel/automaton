---
title: Overview
order: 0
---

# AI Guidance Overview

## Seed-managed documents

Guidance whose source is the embedded seed catalog is synchronized into the runtime database every time the API starts after a deployment. The seed version controls its content and seed-owned metadata; administrators can still enable or disable it, but should use **Clone** to create an editable custom document rather than editing the seed-managed document directly.

If a seed is removed from the catalog, its matching database document is disabled and marked retired. It is not deleted automatically.

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
