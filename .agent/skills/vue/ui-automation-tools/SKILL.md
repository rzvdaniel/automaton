---
title: "Adding UI Automation Tools"
category: "App Editor / AI Preview"
description: "How to add a new ui_* tool so it is correctly registered, routed, and returns real runtime results."
tags: ["ui-automation", "preview", "mcp", "ai", "iframe", "postmessage"]
---

# Adding UI Automation Tools

## Architecture Overview

UI automation tools (`ui_*`) let the AI agent interact with the App Editor preview iframe. The execution pipeline has three layers:

```
AI agent calls tool
       ↓
Backend: UiAutomationTool.cs       — registers the MCP tool + waits for ACK
       ↓  (ACK round-trip)
Frontend: AppEditorView.vue        — detects the tool call, dispatches it
       ↓
Frontend: preview-automation-bridge.ts  — routes to the correct executor
       ↓
  ┌────────────────────────────────────────────────┐
  │  PARENT FRAME (special cases only)             │
  │  • GetConsoleLogs — reads editorStore data     │
  │  • Reload — listens to iframe load event       │
  └────────────────────────────────────────────────┘
       ↓  (postMessage bridge for everything else)
  ┌────────────────────────────────────────────────┐
  │  IFRAME: automaton-api-preview.js              │
  │  Implements all other actions using            │
  │  document.querySelector, window globals, etc.  │
  └────────────────────────────────────────────────┘
       ↓  (postMessage response back to parent)
Frontend: reportUiActionResult     — ACKs result to backend
       ↓
Backend: UiAutomationActionAckService — unblocks the waiting tool call
       ↓
AI agent receives concrete runtime result ✅
```

## Critical Rule: ACK Completes the Loop

The backend `UiAutomationTool.cs` sends an envelope with `clientExecutionRequired: true` and then **blocks** waiting for the frontend to call the ACK endpoint. If the frontend never executes the action and calls `reportUiActionResult`, the backend times out and returns the raw envelope — **which looks like a result but contains no real data**.

A response containing only `clientExecutionRequired: true` is NOT a valid result. It means the frontend did not execute the action.

## Adding a New Tool — Checklist

### 1. Backend: Register the MCP tool (`UiAutomationTool.cs`)

File: `src/Automaton.Mcp/Tools/UiAutomationTool.cs`

Add the tool name constant and add it to `HandledTools`:

```csharp
public const string MyNewToolName = "ui_my_new_tool";

private static readonly HashSet<string> HandledTools =
[
    // ... existing tools ...
    MyNewToolName,
];
```

Add the tool definition in `GetToolsAsync()`:

```csharp
new()
{
    Name = MyNewToolName,
    Capability = ToolCapability.Read,
    Description = "Description of what the tool does in the preview app.",
    InputSchema = JsonDocument.Parse("""
        {"type":"object","properties":{"selector":{"type":"string","description":"CSS selector"}},"required":["selector"]}
        """)
},
```

No other backend changes are needed — `ExecuteToolAsync` handles all tools generically.

### 2. Frontend: Add to `UiAutomationAction` (`preview-automation-bridge.ts`)

File: `src/automaton-studio-vue/src/features/apps/services/preview-automation-bridge.ts`

```typescript
export const UiAutomationAction = {
  // ... existing actions ...
  MyNewTool: 'ui_my_new_tool',
} as const
```

If the tool returns new result fields, add them to `UiAutomationResult`:

```typescript
export interface UiAutomationResult {
  // ... existing fields ...
  myNewField?: string
}
```

**That's all that's needed in this file.** `AppEditorView.vue` uses `Object.values(UiAutomationAction)` to detect UI actions — new entries are picked up automatically.

### 3. Frontend: Implement in the iframe script (`automaton-api-preview.js`)

File: `src/automaton-studio-vue/src/features/apps/scripts/automaton-api-preview.js`

Add the implementation to the `uiActions` object (or equivalent handler map):

```javascript
async ui_my_new_tool(args) {
  const selector = String(args.selector || '').trim();
  if (!selector) return { success: false, error: "Missing required argument 'selector'" };
  const element = document.querySelector(selector);
  if (!element) return { success: false, error: `Element not found for selector: ${selector}` };
  // ... do the work ...
  return { success: true, myNewField: result, details: `Did the thing on ${selector}` };
},
```

### Special Cases: Parent-Frame Only

Only two actions run in the parent frame (not through the bridge):

| Action | Why parent-frame |
|--------|-----------------|
| `GetConsoleLogs` | Reads `editorStore.uiLogEntries` — Vue store data lives in parent |
| `Reload` | Must listen to the iframe `load` event from outside the iframe |

If your new tool needs either of these, handle it as a special `if` branch at the top of `executeUiAutomationAction` in `preview-automation-bridge.ts`, before the final `return executeViaPostMessageBridge(action, args)`.

## Common Mistakes

### ❌ Tool returns `clientExecutionRequired: true` with no other data

The frontend is not executing the tool. Check that:
1. The action string in `UiAutomationAction` exactly matches the tool name registered in the backend
2. `AppEditorView.vue` — `handleAiToolExecuted` uses `Object.values(UiAutomationAction)` (not a hardcoded list)

### ❌ Tool implemented in bridge but result is always `{ success: false, error: "Unsupported UI action" }`

The action is not handled in `automaton-api-preview.js`. Check the handler map there.

### ❌ Tool works in same-origin preview but fails cross-origin

All actions go through `executeViaPostMessageBridge` by default — cross-origin is already handled. If you added a special parent-frame `if` branch, make sure it doesn't break the cross-origin path.

## File Reference

| File | Purpose |
|------|---------|
| `src/Automaton.Mcp/Tools/UiAutomationTool.cs` | MCP tool registration + ACK wait |
| `src/Automaton.Mcp/Tools/UiAutomationActionAckService.cs` | ACK round-trip service |
| `src/automaton-studio-vue/src/features/apps/services/preview-automation-bridge.ts` | `UiAutomationAction` enum, `UiAutomationResult` type, routing |
| `src/automaton-studio-vue/src/features/apps/scripts/automaton-api-preview.js` | All iframe-side action implementations |
| `src/automaton-studio-vue/src/features/apps/views/AppEditorView.vue` | `handleAiToolExecuted` — dispatches and ACKs results |
| `src/Automaton.Studio.Server/Controllers/AiAppConversationController.cs` | ACK endpoint (`reportUiActionResult`) |
