# Autonomous App Building

This document explains how the AI builds, tests, and iterates on web applications autonomously in the Automaton App Editor.

---

## Overview

When a user opens the App Editor, the **Build tab** is the default view. It combines the AI chat, a live embedded preview iframe, and a test/console panel in one layout. The AI can write files, reload the preview, inspect console output, and run functional tests — without any human intervention between those steps.

```
┌─────────────────┬──────────────────────────────────┐
│                 │   Preview iframe (live app)       │
│   AI Chat       │──────────────────────────────────│
│   (35%)         │   TestPanel: results + console    │
│                 │   (collapsible, 65% total)        │
└─────────────────┴──────────────────────────────────┘
```

---

## The Preview Bridge

The core mechanism is a **polling command bridge** between the AI (backend MCP tool) and the browser (frontend Vue composable).

### Architecture

```
AI Model
  │
  │  calls MCP tool (refresh_preview / get_console_logs / run_app_tests)
  ▼
PreviewBridgeTool.cs
  │
  │  SendCommandAsync() — enqueues command, awaits result (30s timeout)
  ▼
PreviewBridgeService.cs  (Singleton, in-memory)
  │  ConcurrentDictionary keyed by "{userId}:{appId}"
  ▼
GET /api/apps/{appId}/preview-bridge/command   ← polled every 300ms by browser
  │
  │  204 = nothing pending
  │  200 = { id, type, payload }
  ▼
usePreviewBridge.ts  (Vue composable, runs in Build tab)
  │
  │  executes command against live iframe
  ▼
POST /api/apps/{appId}/preview-bridge/results/{commandId}  (sends result back)
  │
  ▼
PreviewBridgeService resolves the TaskCompletionSource
  │
  ▼
PreviewBridgeTool returns result to AI
```

One command is pending at a time per `userId:appId`. If no browser is polling (user navigated away), the command times out after 30 seconds and the AI receives an error.

### Key files

| File | Role |
|------|------|
| `Services/PreviewBridgeService.cs` | In-memory queue; `SendCommandAsync`, `PeekCommand`, `TryResolve` |
| `Controllers/PreviewBridgeController.cs` | GET (poll) + POST (result) REST endpoints |
| `Services/Mcp/Apps/PreviewBridgeTool.cs` | Three MCP tools registered in the AI tool pipeline |
| `composables/usePreviewBridge.ts` | 300ms polling loop; executes commands in iframe |
| `components/AppBuildView.vue` | Mounts the composable; wires iframe ref from AppPreviewPanel |

---

## The Three MCP Tools

### `refresh_preview`

Sends a `"refresh"` command to the browser. The composable calls `editorStore.refreshPreview()` which changes the `:key` on the iframe (cache-bust), then POSTs `{ "reloaded": true }` back. The AI always calls this after writing or editing files.

### `get_console_logs`

Sends a `"get_logs"` command. The composable reads `editorStore.consoleLogs` (captured from the iframe) and POSTs them back as a JSON array of `{ level, message, timestamp }` entries.

**How console capture works:** `automaton-api-preview.js` (injected into every preview iframe) intercepts `console.log/warn/error/info`, `window.onerror`, and `unhandledrejection`, and forwards them to the parent frame via `postMessage({ type: 'automatonConsoleLog', data: ... })`. `AppEditorView` listens for this and calls `editorStore.addConsoleLog()`.

### `run_app_tests`

Sends a `"run_tests"` command with the test spec as payload. The composable:

1. Takes `app-test-runner.js` (imported as raw text via `?raw`)
2. Concatenates it with the AI's test spec
3. Injects the combined script as a `<script>` tag into `iframe.contentDocument.body`
4. Listens for a `postMessage({ type: 'automatonTestResults' })` response (30s timeout)
5. POSTs the structured results back to the server

Results are also stored in `editorStore.testResults`, causing the TestPanel to auto-expand.

---

## The Test Framework

`app-test-runner.js` is a self-contained IIFE (~230 lines) — no npm dependency, no CDN. It sets window globals inside the iframe:

```javascript
window.describe(name, fn)          // group tests
window.it(name, asyncFn)           // individual test
window.expect(value)               // assertion builder
window.getByRole(role, { name })   // query by ARIA role
window.getByText(text)             // query by visible text
window.getByTestId(id)             // query by data-testid
window.fireEvent.click(el)         // simulate click (highlights element briefly)
window.fireEvent.input(el, value)  // fill input
window.fireEvent.submit(el)        // trigger form submit
```

**Execution sequencing:** The IIFE runs → sets globals → schedules `setTimeout(0)` → the AI's spec code runs synchronously (registering `describe`/`it` blocks) → the timeout fires → all collected specs execute in order.

**Visual overlay:** During a test run, a floating badge appears in the bottom-right of the iframe showing the current spec name. After all specs complete, it shows a ✅/❌ summary (e.g. "3 passed, 1 failed") for 8 seconds then disappears.

---

## The Build Loop

The system prompt (`app-base.md`) instructs the AI to follow this loop after writing any files:

```
1. write_file / edit_file  — write or update source files
        ↓
2. refresh_preview         — reload iframe with latest files
        ↓
3. get_console_logs        — check for JS errors / exceptions
        │
        ├─ errors found → fix code → goto 1
        │
4. run_app_tests           — inject and run a functional test spec
        │
        ├─ failures found → read error → fix code → goto 1
        │
5. All pass → write/update .instructions/ files → Summary of Changes
```

The AI writes the test spec itself based on what the app is supposed to do. A typical spec for a todo app looks like:

```javascript
window.describe('Todo App', () => {
  window.it('renders the input and add button', () => {
    window.expect(document.querySelector('input[type="text"]')).toBeTruthy();
    window.expect(window.getByRole('button', { name: /add/i })).toBeTruthy();
  });

  window.it('adds a todo item', async () => {
    window.fireEvent.input(document.querySelector('input'), 'Buy milk');
    window.fireEvent.click(window.getByRole('button', { name: /add/i }));
    await new Promise(r => setTimeout(r, 100));
    window.expect(window.getByText('Buy milk')).toBeTruthy();
  });
});
```

---

## User Experience

- The **Build tab** is always the default when opening an app
- The AI chat is on the left; the live preview is on the right — the user watches the app being built in real time
- The **TestPanel** at the bottom is 36px collapsed by default and auto-expands when test results arrive
- The **Console tab** inside TestPanel shows all captured `console.log/warn/error` output from the iframe
- The visual test overlay in the iframe gives a clear "tests are running" signal without requiring the user to look at any panel

---

## Limitations

| Limitation | Detail |
|---|---|
| No persistent interaction | `run_app_tests` fires DOM events but the state resets on the next `refresh_preview` |
| One command at a time | If the AI calls two tools in parallel, the second will overwrite the first in the queue |
| Requires Build tab open | Commands time out after 30s if the user navigates away from the App Editor |
| Same-origin only | The iframe is served same-origin, so `contentDocument` access works — this would not work for externally hosted apps |
| No Playwright / headless browser | Tests run in the live user browser; flaky if the user interacts with the page during a test run |
