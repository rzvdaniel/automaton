# Human-in-the-Loop (HITL) Mode

Human-in-the-Loop mode is Automaton's **collaborative execution model**. The AI proceeds autonomously for safe, reversible work and pauses for user approval before anything complex, risky, or irreversible. Unlike **Autonomous** mode — which plans and implements without stopping — HITL mode is designed for oversight: the user reviews the plan, confirms high-complexity stages, and approves destructive actions before they happen.

---

## Single-Phase Execution

HITL mode runs a single AI conversation. The model plans inline (no separate planning model) and pauses at defined gates to request user confirmation before continuing.

```
User request
      │
      ▼
┌─────────────────────────────────────────────────────┐
│  AI analyses the request                            │
│  • Calls propose_plan (planType="implementation")   │
│  • Writes .plans/IMPLEMENTATION.md                  │
│  ⏸️  PAUSE — renders plan, asks to proceed          │
└─────────────────────────────────────────────────────┘
      │  user approves
      ▼
┌─────────────────────────────────────────────────────┐
│  AI implements Stage 1                              │
│  • Calls propose_plan (planType="stage_complete")   │
│  • low/medium → auto-proceed to next stage          │
│  • high       → ⏸️  PAUSE — ask before continuing  │
└─────────────────────────────────────────────────────┘
      │  repeat per stage
      ▼
┌─────────────────────────────────────────────────────┐
│  Any delete_file call (no confirmed=true)           │
│  🛑  PAUSE — shows preview, requires confirmation   │
└─────────────────────────────────────────────────────┘
      │  user confirms
      ▼
 Done — one summary message to the user
```

All events stream through a single SSE connection. Pauses are enforced server-side in the provider layer — the loop breaks on `"awaitingApproval": true`, not by relying on the model to stop itself.

---

## The Three Gates

### Gate 1 — Plan review (⏸️)

When the user asks for a non-trivial feature, the AI calls `propose_plan` with `planType="implementation"`. The tool writes `.plans/IMPLEMENTATION.md` and returns:

```json
{
  "awaitingApproval": true,
  "instruction": "Plan saved. Render the full plan as markdown and ask whether to proceed."
}
```

The provider loop breaks immediately. The AI renders the plan in one message ("Here's the plan — shall I proceed?") and stops. The user replies, and the conversation continues.

### Gate 2 — High-complexity stage transitions (⏸️ for `high` only)

After finishing each stage the AI calls `propose_plan` with `planType="stage_complete"`. The tool marks the stage checkbox in the markdown file and returns the next stage's complexity:

| Next stage complexity | `awaitingApproval` | Instruction to model |
|----------------------|--------------------|---------------------|
| `low` / `medium` | `false` | "Start the next stage immediately, silently." |
| `high` | `true` | "Summarise what was done and ask the user before continuing." |

Only `high`-complexity transitions pause. Everything else proceeds without interruption.

### Gate 3 — File deletion (🛑 always)

`delete_file` is the only irreversible tool. When called **without** `confirmed=true`, it never touches the filesystem — it returns:

```json
{
  "awaitingApproval": true,
  "action": "delete_file",
  "path": "src/old-module.js",
  "message": "About to permanently delete 'src/old-module.js'. This cannot be undone. Call delete_file again with confirmed=true to proceed, or do nothing to cancel."
}
```

The provider breaks the loop. The AI shows the preview and asks. If the user approves, the AI calls `delete_file` again with `confirmed=true` and the file is removed. This gate applies in **all** modes, including Autonomous.

---

## Server-Side Enforcement

The loop-break is enforced in the provider layer — **not** only via instruction. Models don't reliably stop themselves when instructed to.

After every tool call, each provider checks:

```csharp
if (ProviderHelpers.ToolResultAwaitsApproval(toolName, toolResult))
    shouldBreakForApproval = true;
```

`ToolResultAwaitsApproval` parses the JSON result and checks for `"awaitingApproval": true`:

```csharp
internal static bool ToolResultAwaitsApproval(string toolName, string toolResult)
{
    try
    {
        using var doc = JsonDocument.Parse(toolResult);
        return doc.RootElement.TryGetProperty("awaitingApproval", out var aw) &&
               aw.ValueKind == JsonValueKind.True;
    }
    catch { return false; }
}
```

The check is **tool-agnostic**: any tool returning `"awaitingApproval": true` triggers a pause without any changes to provider code. All providers implement this identically: `OpenAiCompatibleProvider`, `GoogleProvider`, `AnthropicProvider`.

---

## Stage Complexity Levels

Stages are defined in the plan markdown using the format:

```markdown
- [ ] Stage 1 · Setup _(complexity: low)_
- [ ] Stage 2 · Core Logic _(complexity: medium)_
- [ ] Stage 3 · Database Migrations _(complexity: high)_
```

`PlanningTool.FindNextStage` parses this to extract `(Name, Description, Complexity, Index)` for each unchecked stage.

| Complexity | Typical examples | HITL behaviour |
|------------|-----------------|----------------|
| `low` | File scaffolding, config files, static content | Auto-proceed |
| `medium` | Core logic, UI components, API endpoints | Auto-proceed |
| `high` | Architectural changes, DB migrations, multi-system integration | Pause for approval |

### Stage count guidelines

The AI is instructed to keep stage counts conservative:

| Project size | Recommended stages |
|---|---|
| Small app (< 10 files, self-contained) | 1–2 stages |
| Medium app (10–30 files) | 2–3 stages |
| Large app / complex integration | 3–5 stages |

---

## Iteration Limits

HITL mode uses tier-based iteration limits resolved from the detected task complexity:

| Tier | Default limit | Admin override |
|------|--------------|----------------|
| Simple | 20 | `IterationLimits.Simple` |
| Medium | 50 | `IterationLimits.Medium` |
| Large | 100 | `IterationLimits.Large` |
| Enterprise | 200 | `IterationLimits.Enterprise` |

Limits can be overridden in the admin panel under **AI Settings → Iteration Limits**. A global override (`MaxIterations`) applies across all tiers when set.

---

## Comparison with Autonomous Mode

| | Human-in-the-Loop | Autonomous |
|---|---|---|
| Plan review gate | ✅ Pauses for approval | ❌ No pause |
| High-complexity stage gate | ✅ Pauses for approval | ❌ No pause |
| Planning phase | ❌ Model plans inline | ✅ Separate planning model |
| File deletion gate | ✅ Always confirmed | ✅ Same |
| Iteration limit | Tier-based (20–200) | 500 (admin-configurable) |

---

## Relevant Files

| File | Role |
|------|------|
| `src/Automaton.Mcp/Tools/PlanningTool.cs` | `propose_plan` tool: writes plans, marks stages done, computes `awaitingApproval` from stage complexity |
| `src/Automaton.Mcp/Tools/FileIoTool.cs` | `delete_file`: preview-first pattern with `confirmed` flag |
| `src/Automaton.Mcp/Providers/ProviderHelpers.cs` | Shared `ToolResultAwaitsApproval` helper used by all providers |
| `src/Automaton.Mcp/Providers/OpenAiCompatibleProvider.cs` | Loop-break implementation (`breakAfterPlanPresentation` flag) |
| `src/Automaton.Mcp/Providers/GoogleProvider.cs` | Gemini loop-break implementation |
| `src/Automaton.Mcp/Providers/AnthropicProvider.cs` | Anthropic loop-break implementation |
| `src/Automaton.Mcp/Modes/HumanInTheMiddleMode.cs` | System prompt rules: when to plan, stage confirmation behaviour, deletion gate |
| `src/Automaton.Studio.Server/Controllers/AiConversationControllerBase.cs` | `ExecuteStreamAsync` — runs the single-phase conversation; skips planning phase for HITL |
| `docs/ai-chat/autonomous-mode.md` | Documentation for Autonomous two-phase planning mode |

---

## Extending HITL to New Tools

To add a confirmation gate to any new tool:

1. Return `{ "awaitingApproval": true, ... }` from the tool when the action is unconfirmed.
2. Add a `confirmed` (or similar) parameter; perform the action only when it is `true`.
3. No changes needed in any provider — `ProviderHelpers.ToolResultAwaitsApproval` picks it up automatically.
