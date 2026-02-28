# AI Planning & Instructions System

## Problem

The AI starts building immediately after the first user prompt. For medium/large apps this produces fragmented, inconsistent output with no architecture upfront. There is also no way to encode user preferences or coding standards that persist across sessions.

## Proposed Approach

Three interconnected features:

1. **Planning gate** — AI proposes a design plan and implementation plan before writing any code, with user approval at each stage
2. **Instructions folder** — per-resource folder of preference/standard files the AI reads automatically
3. **Generic MCP tools** — resource-agnostic tools usable by apps, steps, and flows

---

## 1. Instructions Folder

### Structure

```
AppData/{appId}/
  .instructions/
    _index.md           ← auto-summary (always injected, ~200 tokens)
    frontend.md         ← framework, component patterns, CSS approach
    backend.md          ← API conventions, error handling
    design.md           ← theme, spacing, color rules
    database.md         ← schema conventions, query patterns
    testing.md          ← test strategy, coverage expectations
    <anything>.md       ← user or AI can add freely
  .plans/
    DESIGN.md           ← architecture plan (written by propose_plan tool)
    IMPLEMENTATION.md   ← implementation plan (written by propose_plan tool)
```

### Frontmatter per instruction file

```markdown
---
priority: high        # high | medium | low
context: always       # always | on-request
tags: [frontend, css]
---
# Frontend Instructions
Always use PrimeVue components...
```

### Injection logic

| `context` value | Injected when |
|----------------|---------------|
| `always`       | Every session — keep these short (<300 tokens each) |
| `on-request`   | When AI calls `read_instruction(filename)` MCP tool |

Only `_index.md` + `always` files are injected into the system prompt automatically. The AI reads the index, decides which `on-request` files are relevant, and calls `read_instruction` for them. This keeps the prompt lean regardless of how many instruction files exist.

### Who writes instruction files?

- **User**: via Monaco editor in a file tree sidebar, or by asking the AI to create them
- **AI**: after building a feature, the AI can update the relevant instruction file to record what it chose and why — creating a living style guide

---

## 2. Planning Gate

### Flow

```
User first prompt
      │
      ▼
Estimate complexity (existing tier system)
      │
      ├─ simple  → skip planning, build directly
      │            (log: "Plan skipped — simple request")
      │
      ├─ medium  → ask user once:
      │            "This looks like a medium task. Plan first? [Yes] [No, build]"
      │            default: Yes
      │
      └─ large/enterprise → always plan, no question
            │
            ▼
      DESIGN.md exists?
      ├─ yes → "Found existing design plan. Use it or revise?"
      │         [Use existing] [Revise]
      └─ no  → AI calls propose_plan(type="design")
                    │
                    ▼
            Frontend renders approval card
            User: [Approve] [Refine: <message>]
                    │ approved
                    ▼
            IMPLEMENTATION.md exists?
            ├─ yes → "Found existing implementation plan. Use it or revise?"
            └─ no  → AI calls propose_plan(type="implementation")
                          │
                          ▼
                  Frontend renders approval card
                  User: [Approve & Build All] [Approve & Build Stage by Stage]
                          │
                          ▼
                    AI implements
```

### Stage-by-stage mode

When user picks "Build Stage by Stage":
- AI implements one stage, then calls `propose_plan(type="stage_complete", stageIndex=N)`
- Frontend renders a mini card: "Stage N complete. Continue? [Yes] [Stop here]"
- Gives user checkpoints without manual micromanagement

### Progress tracking

The AI updates `IMPLEMENTATION.md` after each stage completes — the file becomes a living progress document:

```markdown
## Implementation Plan

- [x] Stage 1 · Foundation — _completed 2026-02-28 by AI_
- [x] Stage 2 · Core Components — _completed 2026-02-28 by AI_  
- [ ] Stage 3 · Data & API
- [ ] Stage 4 · Polish & UX
```

Progress is surfaced in multiple places:
- **Chat**: the original approval card updates in-place as stages tick off — user can scroll up and see current state
- **Stage complete card**: each `stage_complete` tool call renders a card in chat showing files changed and a "Continue" button
- **File tree sidebar**: `2/4` badge on `IMPLEMENTATION.md`
- **App Editor toolbar**: small `Stage 2/4` pill, visible only while an implementation is in progress

Users can also manually check off stages in the `.md` file via Monaco — useful when they implement something by hand outside of AI.

---

## 3. MCP Tools

### `propose_plan`

Generic — the caller provides context, the tool has no opinion on what is being built.

```typescript
propose_plan({
  planType: "design" | "implementation" | "stage_complete",
  title: string,
  summary: string,
  // design plan fields:
  components?: Array<{ name: string, responsibility: string }>,
  techDecisions?: Array<{ decision: string, rationale: string }>,
  // implementation plan fields:
  stages?: Array<{
    index: number,
    name: string,
    description: string,
    files: string[],
    estimatedComplexity: "low" | "medium" | "high"
  }>,
  // stage_complete fields:
  stageIndex?: number,
  stageSummary?: string,
  filesChanged?: string[]
})
```

Side effects:
- Writes `DESIGN.md` or `IMPLEMENTATION.md` to `.plans/` folder
- Pauses streaming — frontend detects the tool call and renders an approval card
- Returns `{ approved: boolean, feedback?: string }` back to the AI

### `read_instruction`

```typescript
read_instruction({
  filename: string   // e.g. "frontend.md"
})
// returns: file contents as string
```

### `write_instruction`

```typescript
write_instruction({
  filename: string,
  content: string,
  reason?: string    // logged in _index.md, shown as chat notification
})
```

AI calls this after making consistent design choices to record them for future sessions.

---

## 4. Frontend — Approval Cards in Chat

When the AI calls `propose_plan`, the chat panel renders a rich card instead of plain text:

### Design Plan Card

```
┌─────────────────────────────────────────────────┐
│  📐 Design Plan                    [Edit in IDE] │
├─────────────────────────────────────────────────┤
│  Components                                      │
│  • Header — top nav, auth state                  │
│  • Dashboard — KPI cards, chart                  │
│  • ...                                           │
├─────────────────────────────────────────────────┤
│  Tech Decisions                                  │
│  • PrimeVue DataTable for lists (not custom)     │
│  • ...                                           │
├─────────────────────────────────────────────────┤
│  [Approve]   [Refine ▼]                          │
└─────────────────────────────────────────────────┘
```

### Implementation Plan Card

```
┌─────────────────────────────────────────────────┐
│  🗺️ Implementation Plan            [Edit in IDE] │
├─────────────────────────────────────────────────┤
│  Stage 1 · Foundation          complexity: low   │
│  Stage 2 · Core Components     complexity: high  │
│  Stage 3 · Data & API          complexity: med   │
│  Stage 4 · Polish & UX         complexity: low   │
├─────────────────────────────────────────────────┤
│  [Approve & Build All]   [Build Stage by Stage]  │
└─────────────────────────────────────────────────┘
```

"Edit in IDE" opens the `.plans/DESIGN.md` or `.plans/IMPLEMENTATION.md` file in Monaco directly.

---

## 5. File Tree Sidebar in App Editor

A thin file tree panel showing the app's instruction and plan files:

```
📁 .instructions/
   📄 _index.md
   📄 frontend.md
   📄 design.md
📁 .plans/
   📄 DESIGN.md
   📄 IMPLEMENTATION.md
```

Clicking any file opens it in Monaco. New file button lets users add instruction files. The AI can also be asked: "Create a testing instruction file" and it will write it via `write_instruction`.

---

## 6. Generic by Design

All MCP tools (`propose_plan`, `read_instruction`, `write_instruction`) are resource-agnostic. The system prompt builder for any resource type (app, step, flow, or anything added in future) simply points the tools at the right working directory. The tools themselves have no knowledge of what they are building.

The `.instructions/` and `.plans/` folder convention works identically wherever it is placed.

---

## Implementation Order

- [ ] `propose_plan` MCP tool — writes `.plans/*.md`, returns structured data to stream
- [ ] `read_instruction` + `write_instruction` MCP tools
- [ ] Instruction injection logic in system prompt builder (always + on-request)
- [ ] Complexity-aware planning gate in `AiAppConversationController` pre-build check
- [ ] Frontend: approval card components (`DesignPlanCard.vue`, `ImplementationPlanCard.vue`)
- [ ] Frontend: detect plan tool calls in chat stream, render cards instead of text
- [ ] File tree sidebar in App Editor (`AppFilesTree.vue`)
- [ ] Step editor: wire same tools + sidebar
- [ ] `_index.md` auto-update when instruction files are written

## Decisions

- **Instruction file updates**: AI updates silently, shown in chat as a small notification (e.g. "📝 Updated `frontend.md`")
- **Plan file diffs**: No diff view — keep it simple. App history tab already supports full restore including `.instructions/` and `.plans/` folders
- **MCP tools scope**: Tools are fully generic — no `resourceType` enum baked in. The caller (app, step, flow, or anything future) passes its own context. Tools work identically regardless of what is being built
- **Token budget**: 1000 token hard cap for injected instructions, trimmed by `priority` (high first, then medium, then low until cap is reached)
