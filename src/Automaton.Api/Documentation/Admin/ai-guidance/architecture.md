---
title: Architecture
order: 10
---

# AI Guidance Architecture

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

## Runtime Loading

### Studio

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

### CLI

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

### MCP

The MCP guidance tool exposes only discoverable guidance.

The AI can:

```text
list_automaton_guidance()
```

```text
get_automaton_guidance(key)
```

Base Prompts are never listed.

## CLI Export

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
