# Autonomous Mode

Autonomous mode is Automaton's **fully hands-off execution model**. The AI plans and implements an entire task from start to finish without asking for user confirmation at any point. Unlike **[Human-in-the-Loop (HITL)](human-in-the-loop-mode.md)** mode — which pauses at plan review and high-complexity stage transitions — Autonomous mode is designed for trust: the user fires a request and the AI delivers the result.

---

## Two-Phase Execution

When a conversation starts in Autonomous mode the server runs **two sequential AI conversations** before returning any result.

```
User request
      │
      ▼
┌─────────────────────────────────────────────────────┐
│  Phase 1 · Planning (planning model)                │
│  • Calls propose_plan once                          │
│  • Writes .plans/IMPLEMENTATION.md                  │
│  • Writes ONE confirmation sentence then stops      │
│  • Max 5 iterations                                 │
└─────────────────────────────────────────────────────┘
      │  plan file exists
      ▼
┌─────────────────────────────────────────────────────┐
│  Phase 2 · Implementation (user-selected model)     │
│  • Reads .plans/IMPLEMENTATION.md first             │
│  • Implements every file in the plan                │
│  • No pauses, no confirmations                      │
│  • Up to 500 iterations (admin-configurable)        │
└─────────────────────────────────────────────────────┘
      │
      ▼
 Done — one summary message to the user
```

Both phases stream their events through the same SSE connection, so the user sees the plan being written and then the implementation proceeding in real-time.

---

## Phase 1 — Planning

### Model selection

The planning model is the provider model with **`IsPlanningModel = true`** in the database (configured in the admin AI Models panel). It does not have to be the same provider as the user's chosen implementation model.

If no planning model is configured, Phase 1 is skipped and the implementation model runs directly (same behaviour as the previous single-phase autonomous mode).

### What the planning model does

The planning model receives:

- The **app-specific system prompt** (same base as the implementation conversation)
- A **planning-phase instruction block** appended to that prompt, which:
  - Tells the model it is in a planning phase and a separate agent will implement the plan
  - Instructs it to call `propose_plan` exactly once
  - Defines detailed quality requirements for the plan (see below)

### Plan quality requirements

The planning prompt explicitly requires:

| Section | What to include |
|---------|----------------|
| **File inventory** | Every file to create or modify, with exact relative paths |
| **Directory tree** | Complete structure of what will exist after implementation |
| **CSS design** | Exact colors (hex/rgb), class names, layout approach (grid/flex), responsive breakpoints |
| **JS/TS architecture** | Module responsibilities, key function signatures, data flow |
| **Data structures** | Interfaces, types, schema definitions with exact field names and types |
| **Component hierarchy** | Parent-child relationships, props, events |
| **API contracts** | Endpoint URLs, HTTP methods, request/response shapes |
| **Load order** | Which files import which, in what order they must load |
| **Technical decisions** | Chosen libraries and patterns, with reasons |
| **Known pitfalls** | Specific edge cases (e.g. "dispose event listeners on unmount") |
| **Exact content** | Full file content for small config/boilerplate files |

The intent is that the implementation model should be able to build the entire feature correctly from the plan alone, without needing to re-interpret the original user message.

### PlanningTool behaviour in autonomous phase

`propose_plan` detects the `planningPhase = "autonomous"` key in the conversation context and changes behaviour:

| Field | Normal (HITL) | Autonomous planning phase |
|-------|---------------|--------------------------|
| `awaitingApproval` | `true` | `false` |
| `instruction` | "Render the full plan… ask whether to proceed" | "Plan saved. Write ONE brief confirmation sentence and stop." |

Because `awaitingApproval` is `false`, the provider loop does **not** set `breakAfterPlanPresentation`. The planning conversation exits naturally when the model produces no more tool calls.

---

## Phase 2 — Implementation

After a successful planning phase, the server augments the implementation model's system prompt with:

```
## FOLLOW THE IMPLEMENTATION PLAN

A detailed implementation plan has already been prepared for this task.
Call `read_file` on `.plans/IMPLEMENTATION.md` as your FIRST action.
Implement every file, structure, and decision specified in the plan exactly.
Do NOT call `propose_plan` — the plan is already written.
```

The implementation then runs normally using the user-selected model (or the admin-configured default model). Stage-by-stage confirmations do not apply — `AutonomousMode.IsToolAllowed` allows all tools and the system prompt forbids intermediate pauses.

---

## Iteration Limits

| Phase | Limit | Configurable? |
|-------|-------|---------------|
| Planning | 5 iterations (default) | Yes — admin `IterationLimits.Planning` |
| Implementation | 500 (default) | Yes — admin `IterationLimits.Autonomous` |

The implementation limit can be overridden in the admin panel under **AI Settings → Iteration Limits → Autonomous**.

---

## Fallback Behaviour

| Condition | Behaviour |
|-----------|-----------|
| No `IsPlanningModel` model configured | Phase 1 skipped; implementation runs directly |
| No API key for the planning provider | Phase 1 skipped; warning logged |
| Planning phase fails (provider error) | Phase 1 skipped; warning logged; implementation runs directly |

In all three cases Autonomous mode still works — it just does not benefit from the two-phase planning step.

---

## Comparison with Human-in-the-Loop Mode

| | Autonomous | Human-in-the-Loop |
|---|---|---|
| Plan review gate | ❌ No pause | ✅ Pauses for approval |
| High-complexity stage gate | ❌ No pause | ✅ Pauses for approval |
| Planning phase | ✅ Separate model | ❌ Model plans inline |
| File deletion gate | ✅ Same (always confirmed) | ✅ Same |
| Iteration limit | 500 | Tier-based (Simple→Enterprise) |

---

## Relevant Files

| File | Role |
|------|------|
| `src/Automaton.Studio.Server/Controllers/AiConversationControllerBase.cs` | `ExecuteStreamAsync` orchestrates both phases; `RunAutonomousPlanningPhaseAsync` drives Phase 1; `BuildPlanningPhaseSystemPrompt` constructs the planning-phase instructions |
| `src/Automaton.Mcp/Tools/PlanningTool.cs` | Detects `planningPhase = "autonomous"` context key; returns `awaitingApproval: false` and a stop instruction |
| `src/Automaton.Mcp/Modes/AutonomousMode.cs` | System prompt rules for implementation: never call `propose_plan`, no intermediate pauses, read plan first |
| `src/Automaton.Studio.Server/Services/AiProviderMetadataService.cs` | `GetGlobalPlanningModelAsync()` — resolves the `IsPlanningModel` model across all providers |
| `src/automaton-studio-vue/src/shared/components/ui/AiModeModelSelector.vue` | UI component: mode toggle (Autonomous / Human-in-the-Middle) + model selector |
| `docs/ai-chat/human-in-the-loop-mode.md` | Documentation for the HITL collaborative execution mode |
