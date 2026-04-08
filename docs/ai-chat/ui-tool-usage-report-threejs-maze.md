# UI Tool Usage Report — Three.js Maze Explorer

## Overview

This report analyses a real AI agent session where a Three.js Maze Explorer app was built and debugged autonomously. The session log was parsed to count tool calls, identify behavioral phases, and surface optimization opportunities.

---

## Session Summary

| Metric | Value |
|---|---|
| Total tool calls | 49 |
| UI tool calls | 22 (45%) |
| Non-UI tool calls | 27 (55%) |
| Total tokens | 525,800 |
| Cached tokens | 474,800 |
| Cache hit rate | 93.3% |
| Fresh (uncached) tokens | 34,100 |

---

## Tool Call Breakdown

### All Tools

| Tool | Calls | Category |
|---|---|---|
| `edit_file` | 13 | File I/O |
| `ui_assert` | 6 | UI |
| `write_file` | 6 | File I/O |
| `ui_get_console_logs` | 5 | UI |
| `search_file` | 4 | File I/O |
| `ui_wait_for` | 4 | UI |
| `read_file` | 3 | File I/O |
| `ui_click` | 2 | UI |
| `ui_get_page_snapshot` | 2 | UI |
| `ui_navigate` | 2 | UI |
| `list_files` | 1 | File I/O |
| `ui_read_text` | 1 | UI |

### UI Tools Only (22 calls)

| Tool | Calls | % of UI calls |
|---|---|---|
| `ui_assert` | 6 | 27% |
| `ui_get_console_logs` | 5 | 23% |
| `ui_wait_for` | 4 | 18% |
| `ui_click` | 2 | 9% |
| `ui_get_page_snapshot` | 2 | 9% |
| `ui_navigate` | 2 | 9% |
| `ui_read_text` | 1 | 5% |

---

## Behavioral Phases

The AI agent's verification behaviour clustered into four distinct phases.

### Phase 1 — Initial Load Check
After creating the app files, the agent verified the page loaded correctly:

```
ui_navigate → ui_wait_for → ui_get_console_logs
```

3 tool calls. Establishes baseline: page loads and no console errors.

---

### Phase 2 — Game Start Flow
The agent interacted with the app to verify the core user journey:

```
ui_navigate → ui_wait_for → ui_click (Start) → ui_wait_for → ui_get_console_logs →
ui_get_page_snapshot → ui_assert (canvas visible) → ui_assert (score display) →
ui_assert (no error overlay) → ui_read_text (score)
```

~10 tool calls. Full interaction flow to confirm the game starts and renders the 3D maze.

---

### Phase 3 — Post-Fix Re-Verification *(near-duplicate of Phase 2)*
After fixing a JavaScript error, the agent repeated almost the full Phase 2 sequence:

```
ui_navigate → ui_wait_for → ui_click (Start) → ui_wait_for → ui_get_console_logs →
ui_get_page_snapshot → ui_assert (canvas visible) → ui_assert (score display)
```

~8 tool calls. **This is the inefficiency.** After a targeted fix, the agent re-ran the full interaction flow when a quick reload + error log check would have been sufficient.

---

### Phase 4 — Deep Assertions
Final gate before declaring completion. Thorough checks across multiple UI elements:

```
ui_assert × 3 → ui_get_console_logs (final clean check)
```

4 tool calls. Appropriate depth for a final gate.

---

## Key Finding: Phase 2 / Phase 3 Redundancy

Phase 3 is 80% identical to Phase 2. The agent's "post-fix verification" unnecessarily repeats the full user-journey sequence when the fix was a targeted JavaScript change.

**Wasted calls in Phase 3**: ~6–8 calls that could be replaced by 2:
- `ui_reload` → wait for iframe load
- `ui_get_console_logs({ minLevel: "error" })` → confirm no JS errors

### Estimated Savings

| Metric | Before | After (with ui_reload) |
|---|---|---|
| UI tool calls per fix cycle | ~8 | ~2 |
| Reduction | — | ~75% |
| Total UI calls (2 fix cycles) | ~16 | ~4 |

---

## Token Analysis

The high total token count (525K) is expected for a stateless API. Each call re-sends the full conversation history. The 93.3% cache hit rate confirms the system prompt and stable message prefix are cached correctly — this is healthy behavior.

### Token Sources (approximate)

| Source | Tokens |
|---|---|
| System prompt (`app-base.md` ~6,050 + `app-simple.md` ~850) | ~6,900 per call |
| 25 API calls × 6,900 | ~172,500 |
| Growing tool result history | ~300,000+ |
| **Total** | **~525,800** |

The system prompt contribution is fixed. Growth is dominated by accumulated tool results — especially file reads and page snapshots included in the conversation history.

---

## Optimizations Applied

Based on this analysis, two changes were made:

### 1. `ui_reload` Tool
A new `ui_reload` MCP tool was added to reload the preview iframe in a single call, replacing the `ui_navigate → ui_wait_for` pattern used for mid-loop re-checks.

**Before:**
```
ui_navigate(url) → ui_wait_for(selector)   // 2 calls
```

**After:**
```
ui_reload({ waitForSelector: "body" })     // 1 call
```

Registered in: `src/Automaton.Mcp/Tools/UiAutomationTool.cs`  
Implemented in: `src/automaton-studio-vue/src/features/apps/services/preview-automation-bridge.ts`  
Implemented in: `src/automaton-studio-vue/src/features/apps/scripts/automaton-api-preview.js`

### 2. Two-Tier Verification Pattern (System Prompt)
The `app-base.md` AUTONOMOUS COMPLETION LOOP section was updated with an explicit rule:

> **After each fix:** Quick check only — `ui_reload` + `ui_get_console_logs({ minLevel: "error" })`  
> **Final gate only:** Full interaction flow — navigate → wait → interact → assert  
> **Rule:** Never repeat the full interaction flow after a fix.

Updated in: `src/Automaton.Mcp/SystemPrompts/AppPrompts/app-base.md`

---

## Conclusion

The Three.js Maze Explorer session revealed a recurring pattern where the AI agent over-verifies after targeted fixes. The root cause is the absence of a lightweight "reload and check errors" primitive — the agent defaulted to the only verification pattern it had: the full interaction flow.

Adding `ui_reload` and the two-tier verification rule directly addresses this, with an estimated 75% reduction in UI tool calls per fix cycle.
