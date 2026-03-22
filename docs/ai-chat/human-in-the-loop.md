# Human-in-the-Loop (HITL)

Automaton's AI chat uses a **Human-in-the-Loop** pattern to balance autonomous execution with user control. The AI proceeds automatically for safe, reversible work and pauses for user approval before anything complex, risky, or irreversible.

---

## The Three Tiers

| Tier | When | Behaviour |
|------|------|-----------|
| ✅ **Auto-proceed** | Small/medium stages, file writes, edits, reads | AI continues without interruption |
| ⏸️ **Pause for approval** | New implementation plans, high-complexity stage transitions | AI breaks the tool loop, sends one summary message, waits |
| 🛑 **Confirm before acting** | File deletion | AI shows a preview and asks; only deletes after explicit user confirmation |

---

## How It Works End-to-End

### 1. Planning gate (⏸️)

When the user asks for a non-trivial feature, the AI calls `propose_plan` with `planType="implementation"`. The tool writes `.plans/IMPLEMENTATION.md` and returns:

```json
{ "awaitingApproval": true, "instruction": "Plan saved. Summarise it briefly and ask whether to proceed." }
```

The provider loop breaks. The AI writes one message ("Here's the plan — shall I proceed?") and stops. The user replies, and execution continues.

### 2. Stage transitions (⏸️ for `high` only)

After finishing each stage the AI calls `propose_plan` with `planType="stage_complete"`. The tool updates the markdown checkbox and returns the next stage's complexity:

- **`low` / `medium` complexity** → `awaitingApproval: false`. The instruction tells the AI to start the next stage immediately, silently.
- **`high` complexity** → `awaitingApproval: true`. The instruction tells the AI to summarise what was done and ask the user before continuing.

### 3. File deletion gate (🛑)

`delete_file` is the only irreversible tool. When called **without** `confirmed=true`, it never touches the filesystem — it returns:

```json
{
  "awaitingApproval": true,
  "action": "delete_file",
  "path": "src/old-module.js",
  "message": "About to permanently delete 'src/old-module.js'. This cannot be undone. Call delete_file again with confirmed=true to proceed, or do nothing to cancel."
}
```

The provider breaks the loop. The AI asks the user. If they approve, the AI calls `delete_file` again with `confirmed=true` and the file is removed.

---

## Server-Side Enforcement

The loop-break is enforced in the provider layer — **not** only via instruction. This is intentional: models don't reliably follow instructions to stop themselves.

After every tool call, each provider checks:

```csharp
if (ProviderHelpers.ToolResultAwaitsApproval(toolName, toolResult))
    shouldBreakForApproval = true;
```

`ToolResultAwaitsApproval` simply parses the JSON result and checks for `"awaitingApproval": true`:

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

The check is **tool-agnostic**: any tool returning `"awaitingApproval": true` triggers the pause without any changes to the provider code.

All three providers implement this identically: `GoogleProvider`, `OpenAiProvider`, `AnthropicProvider`.

---

## Stage Complexity Levels

Defined in the plan markdown using the format:

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
| `high` | Architectural changes, migrations, multi-system integration | Pause for approval |

---

## Stage Count Guidelines

The AI is instructed to keep stage counts conservative (enforced in `AgentMode.SystemPromptSuffix` and `PlanningTool` description):

| Project size | Recommended stages |
|---|---|
| Small app (< 10 files, self-contained) | 1–2 stages max |
| Medium app (10–30 files) | 2–3 stages |
| Large app / complex integration | 3–5 stages |

---

## Relevant Files

| File | Role |
|------|------|
| `src/Automaton.Mcp/Tools/PlanningTool.cs` | `propose_plan` tool: writes plans, marks stages done, returns `awaitingApproval` |
| `src/Automaton.Mcp/Tools/FileIoTool.cs` | `delete_file`: preview-first pattern with `confirmed` flag |
| `src/Automaton.Mcp/Providers/ProviderHelpers.cs` | Shared `ToolResultAwaitsApproval` helper |
| `src/Automaton.Mcp/Providers/GoogleProvider.cs` | Gemini loop-break implementation |
| `src/Automaton.Mcp/Providers/OpenAiProvider.cs` | OpenAI loop-break implementation |
| `src/Automaton.Mcp/Providers/AnthropicProvider.cs` | Anthropic loop-break implementation |
| `src/Automaton.Mcp/Modes/AgentMode.cs` | System prompt rules: when to plan, when to auto-proceed, deletion confirmation |

---

## Extending HITL to New Tools

To add a confirmation gate to any new tool:

1. Return `{ "awaitingApproval": true, ... }` from the tool when the action is unconfirmed.
2. Add a `confirmed` (or similar) parameter; perform the action only when it is `true`.
3. No changes needed in any provider — `ProviderHelpers.ToolResultAwaitsApproval` picks it up automatically.
