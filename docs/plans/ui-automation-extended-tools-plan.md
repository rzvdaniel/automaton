# UI Automation Extended Tools Plan

## Goal

Extend the existing `ui_*` MCP tool set with the missing primitives needed for an AI agent to autonomously complete an application — covering keyboard input, runtime JS evaluation, DOM attribute inspection, list querying, form controls (select, checkbox), hover, scroll, and canvas/WebGL verification.

---

## Background

The current UI tool set covers the DOM happy-path well: navigate, click, type into text inputs, wait, assert text/existence, and read console logs. Four recurring gaps were identified from the Three.js Maze Explorer session and the analysis in `docs/ai-chat/ui-tool-usage-report-threejs-maze.md`:

1. No way to inspect runtime state (JS variables, component stores, WebGL frames)
2. No way to press keyboard keys — games, modals, keyboard-driven UIs all require this
3. No way to read element attributes (`disabled`, `checked`, `class`, `src`, `data-*`)
4. No way to count or enumerate repeated DOM elements (lists, tables, grid items)

Tier 2 and Tier 3 tools cover less common but valuable scenarios.

---

## Architecture

All new tools follow the identical pattern established by existing tools. No new patterns or services are needed:

- **C# backend** — `UiAutomationTool.cs`: add constant, `HandledTools` entry, `McpTool` definition. No execution logic needed (all tools dispatch to frontend via `_ackService`).
- **TypeScript bridge** — `preview-automation-bridge.ts`: add to `UiAutomationAction` const, handle in `executeUiAutomationAction` switch (both direct-DOM and cross-origin paths).
- **Preview script** — `automaton-api-preview.js`: add `case 'ui_xxx':` to `executePreviewAutomationAction` switch.
- **System prompt** — optionally update `app-base.md` to teach the AI when to use each new tool.

---

## Tools Inventory

### Tier 1 — High impact

| Tool | Description |
|---|---|
| `ui_evaluate` | Execute a JS expression in the preview and return the result |
| `ui_keyboard` | Dispatch one or more key events (keydown/keyup/keypress) to the document or a selector |
| `ui_get_attribute` | Read one or more attributes from an element |

### Tier 2 — Medium impact

| Tool | Description |
|---|---|
| `ui_query_all` | Count + optionally extract text from all elements matching a selector |
| `ui_select` | Set a `<select>` element value |
| `ui_hover` | Trigger `mouseenter`/`mouseover` on an element |
| `ui_scroll_to` | Scroll an element into view |

### Tier 3 — Specialised

| Tool | Description |
|---|---|
| `ui_check` | Check or uncheck a checkbox / radio button |
| `ui_get_canvas_data` | Sample pixel data from a `<canvas>` to confirm something rendered |
| `ui_network_log` | Return captured fetch/XHR calls emitted from the preview |

---

## Implementation

### Phase 1 — Tier 1 tools

#### `ui_evaluate`

**Purpose:** Execute a JS expression in the preview and return a serialised result.  
**Example:** `ui_evaluate({ expression: "window.score" })` → `{ success: true, value: "42" }`

C# schema:
```json
{
  "type": "object",
  "properties": {
    "expression": { "type": "string", "description": "JS expression to evaluate (must be synchronous and JSON-serialisable)" },
    "timeoutMs":  { "type": "number",  "description": "Max wait time for postMessage ack (default 5000)" }
  },
  "required": ["expression"]
}
```

Bridge (same-origin): call `doc.defaultView.eval(expression)`, serialize result with `JSON.stringify`, catch errors.  
Bridge (cross-origin): postMessage, `eval()` on iframe side, respond via `window.parent.postMessage`.

**Security note:** The preview is a sandboxed iframe running user-authored code. `eval` is already in scope there. This tool does not expose the parent Automaton app to evaluation — only the isolated preview document.

---

#### `ui_keyboard`

**Purpose:** Dispatch key events to simulate keyboard input for games, modals, and keyboard-driven UIs.  
**Example:** `ui_keyboard({ key: "ArrowUp" })` or `ui_keyboard({ key: "Enter", selector: "#confirm-btn" })`

C# schema:
```json
{
  "type": "object",
  "properties": {
    "key":      { "type": "string", "description": "Key name per KeyboardEvent.key spec (e.g. 'Enter', 'Escape', 'ArrowUp', ' ')" },
    "selector": { "type": "string", "description": "Optional CSS selector to focus before dispatching (default: document.body)" },
    "repeat":   { "type": "number", "description": "Number of times to dispatch (default 1)" },
    "ctrlKey":  { "type": "boolean" },
    "shiftKey": { "type": "boolean" },
    "altKey":   { "type": "boolean" }
  },
  "required": ["key"]
}
```

Implementation: `new KeyboardEvent('keydown', { key, bubbles: true, ctrlKey, ... })` dispatched on target element or `document.body`. Dispatch `keydown` + `keypress` + `keyup` in sequence.

---

#### `ui_get_attribute`

**Purpose:** Read one or more HTML attributes from an element.  
**Example:** `ui_get_attribute({ selector: "#submit-btn", attributes: ["disabled", "class"] })`

C# schema:
```json
{
  "type": "object",
  "properties": {
    "selector":   { "type": "string", "description": "CSS selector" },
    "attributes": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Attribute names to read (e.g. ['disabled','class','aria-label'])"
    }
  },
  "required": ["selector", "attributes"]
}
```

Implementation: `element.getAttribute(name)` for each attribute; return `{ selector, attributes: { name: value|null } }`.

---

### Phase 2 — Tier 2 tools

#### `ui_query_all`

**Purpose:** Return the count of matching elements and optionally their text content.  
**Example:** `ui_query_all({ selector: "tr.leaderboard-row", includeText: true })`

C# schema:
```json
{
  "type": "object",
  "properties": {
    "selector":    { "type": "string" },
    "includeText": { "type": "boolean", "description": "Include text of each element (default false)" },
    "limit":       { "type": "number",  "description": "Max elements to return text for (default 20)" }
  },
  "required": ["selector"]
}
```

Implementation: `Array.from(doc.querySelectorAll(selector))` — return count; slice to `limit` and map `textContent` if `includeText`.

---

#### `ui_select`

**Purpose:** Set a `<select>` element's value and fire change events.  
**Example:** `ui_select({ selector: "#difficulty", value: "hard" })`

C# schema:
```json
{
  "type": "object",
  "properties": {
    "selector": { "type": "string" },
    "value":    { "type": "string", "description": "The option value to select" }
  },
  "required": ["selector", "value"]
}
```

Implementation: `element.value = value`, dispatch `input` + `change` events.

---

#### `ui_hover`

**Purpose:** Trigger hover/focus events on an element to reveal tooltips, dropdowns, sub-menus.  
**Example:** `ui_hover({ selector: "#help-icon" })`

C# schema:
```json
{
  "type": "object",
  "properties": {
    "selector": { "type": "string" }
  },
  "required": ["selector"]
}
```

Implementation: dispatch `mouseover`, `mouseenter`, `focus` on the target element.

---

#### `ui_scroll_to`

**Purpose:** Scroll an element into view — needed when the UI lazy-renders content below the fold.  
**Example:** `ui_scroll_to({ selector: "#results-section" })`

C# schema:
```json
{
  "type": "object",
  "properties": {
    "selector": { "type": "string" },
    "behavior": { "type": "string", "enum": ["smooth", "instant"], "description": "Scroll behavior (default: instant)" }
  },
  "required": ["selector"]
}
```

Implementation: `element.scrollIntoView({ behavior, block: 'start' })`.

---

### Phase 3 — Tier 3 tools

#### `ui_check`

**Purpose:** Set the `checked` state of a checkbox or radio button.  
**Example:** `ui_check({ selector: "#agree-terms", checked: true })`

C# schema:
```json
{
  "type": "object",
  "properties": {
    "selector": { "type": "string" },
    "checked":  { "type": "boolean", "description": "Target state (default true)" }
  },
  "required": ["selector"]
}
```

Implementation: `element.checked = checked`, dispatch `change`.

---

#### `ui_get_canvas_data`

**Purpose:** Sample pixel data from a `<canvas>` element to confirm something rendered (non-black frame).  
**Example:** `ui_get_canvas_data({ selector: "#game-canvas" })`

C# schema:
```json
{
  "type": "object",
  "properties": {
    "selector":   { "type": "string", "description": "Canvas CSS selector (default: 'canvas')" },
    "sampleSize": { "type": "number", "description": "NxN pixel grid to sample (default 4, max 16)" }
  }
}
```

Implementation: `canvas.getContext('2d').getImageData(...)`, sample `NxN` grid across the canvas area, return `{ hasNonBlackPixels: bool, sampleAlpha: number, width, height }`. WebGL canvases: use `getContext('webgl')` → `readPixels`.

---

#### `ui_network_log`

**Purpose:** Return captured fetch/XHR calls from the preview. Requires the preview script to intercept `window.fetch` and `XMLHttpRequest` and buffer requests in an in-memory log (similar to how console logs are buffered today).

**Example:** `ui_network_log({ limit: 20, urlPattern: "/api/flows" })`

C# schema:
```json
{
  "type": "object",
  "properties": {
    "limit":      { "type": "number", "description": "Max entries to return (default 20)" },
    "urlPattern": { "type": "string", "description": "Optional substring to filter by URL" }
  }
}
```

Implementation (preview side): Monkey-patch `window.fetch` and `XMLHttpRequest.open` in `automaton-api-preview.js` at init time to push to a `_networkLog` array. The tool reads and returns from that array.  
Implementation (bridge side): `ui_network_log` queries the `_networkLog` via `ui_evaluate`-style postMessage or a dedicated `case`.

---

## Files to Change

| File | Change |
|---|---|
| `src/Automaton.Mcp/Tools/UiAutomationTool.cs` | Add constant, HandledTools entry, McpTool definition for each new tool |
| `src/automaton-studio-vue/src/features/apps/services/preview-automation-bridge.ts` | Add to `UiAutomationAction` const + switch case for each tool (direct-DOM path) |
| `src/automaton-studio-vue/src/features/apps/scripts/automaton-api-preview.js` | Add switch case for each tool (cross-origin postMessage path). Add `_networkLog` buffer + fetch/XHR interceptor for `ui_network_log` |
| `src/Automaton.Mcp/SystemPrompts/AppPrompts/app-base.md` | Add tool reference entries for `ui_evaluate`, `ui_keyboard`, and `ui_get_canvas_data` in the verification section |

No new services, interfaces, or DI registrations are required. The ack/postMessage bridge already handles arbitrary action names.

---

## Success Criteria

- AI agent can confirm a WebGL/Canvas scene actually rendered (`ui_get_canvas_data`)
- AI agent can verify runtime JS state without scraping the DOM (`ui_evaluate`)
- AI agent can interact with keyboard-driven UIs and games (`ui_keyboard`)
- AI agent can verify disabled states, ARIA labels, CSS classes (`ui_get_attribute`)
- AI agent can count and read list/table contents (`ui_query_all`)
- All existing UI tools continue to function without regression
- `npm run type-check` passes after each phase
- `dotnet build` passes after each phase
