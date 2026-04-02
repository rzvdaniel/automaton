# Autonomous App Build v2 — Live Visible Build + UI MCP Testing

## Goal

Enable users to **watch AI build and test apps live inside App Editor**:

- Left side: live app preview + live UI logs
- Right side: existing AI chat panel (unchanged mental model)
- Build workspace with two sub-modes:
  - **Visual** (preview-first, default)
  - **Code** (current Monaco + file tree workflow)

AI must be able to interact with the preview app (click/type/navigate/read) and use logs/assertions to fix issues iteratively.

Related implementation details for preview isolation and persistence-safe testing:

- [Preview Test Run Isolation (`testRunId`) — How It Works](./preview-test-run-isolation.md)
- [Mock Backend in Preview Testing](./mock-backend-preview-testing.md)

---

## Guiding Principles (OO + Clean + Extensible)

1. **No new transport layer**: reuse existing SSE conversation stream (`AiAppConversationController` + `AiConversationControllerBase`).
2. **Open/Closed design**: add UI MCP tools via registry/factory, not switch/if chains.
3. **Single responsibility** per class/module:
   - tool contract
   - execution engine
   - event publishing
   - UI rendering
4. **DTO-first boundaries** between backend and frontend.
5. **Strong typing end-to-end** (C# request/response DTOs + TypeScript interfaces).
6. **Observability by design**: every UI action produces structured telemetry.
7. **Safe execution model**: explicit allow-list for actions/selectors, bounded timeouts, explicit errors.

---

## Current Assets to Reuse

- Streaming AI conversation endpoint and SSE client.
- Existing `AiAssistant` tool event rendering.
- Preview iframe + `postMessage` integration in `AppEditorView.vue`.
- Existing `consoleEntries` + `ConsolePanel` types.
- Existing preview API injection script (`automaton-api-preview.js`).

---

## High-Level Architecture

### Backend (C#)

Add a dedicated **UI MCP tool package** under app conversation tooling:

1. `IUiAutomationTool`  
   - Contract for one UI tool (name, schema, execute).

2. `UiAutomationToolRegistry`  
   - Registers available UI tools.
   - Resolves by tool name.
   - Enables easy future addition (no core pipeline edits).

3. `UiAutomationOrchestrator`  
   - Validates request/context.
   - Dispatches tool execution.
   - Emits structured progress/events.

4. `UiAutomationSessionService`  
   - Tracks per-session test run state.
   - Correlates AI tool call IDs ↔ frontend action results.

5. `UiAutomationDtos`  
   - Typed request/response models for action envelope and result envelope.

6. `AppMcpToolProvider` extension  
   - Expose new tools to AI through existing MCP provider pattern.

### Frontend (Vue/TS)

Add a **Preview Automation Bridge** and Build visual workspace:

1. `BuildVisualWorkspace.vue`  
   - Left: embedded preview + inline live logs.
   - Right: existing AI/History panel.

2. `PreviewAutomationBridge` (TS module/service)  
   - Executes requested actions against preview iframe:
     - click, type, navigate, readText, assert, queryElements
   - Returns normalized result payloads.

3. `PreviewEventCollector` (TS module/service)  
   - Collects:
     - `console.log/warn/error`
     - `window.onerror`
     - `unhandledrejection`
     - resource/network failures (where available)
   - Sends structured events via existing `postMessage` flow.

4. `BuildLiveLogStore` (or extend existing app editor store)
   - Rolling log buffer, filter, severity, source, correlation IDs.

5. `BuildModeState` in App Editor
   - `buildViewMode: 'visual' | 'code'`
   - Persist in localStorage.

---

## UI MCP Tool Set (v1)

All tools should be small, typed, and independent.

1. `ui_navigate`
2. `ui_click`
3. `ui_type`
4. `ui_read_text`
5. `ui_read_attribute`
6. `ui_wait_for`
7. `ui_assert`
8. `ui_get_console_logs`
9. `ui_get_page_snapshot` (lightweight DOM snapshot for debugging)

Each tool should return:
- `success`
- `action`
- `durationMs`
- `details`
- `warnings`
- `error` (explicit, never swallowed)
- `correlationId`

---

## Interaction Flow

1. User prompts AI in App Editor (existing chat panel).
2. AI edits files with existing MCP tools.
3. App preview refreshes (existing behavior).
4. AI invokes UI MCP tool (e.g., `ui_click`).
5. Backend streams tool call event over existing SSE.
6. Frontend bridge executes action in preview iframe.
7. Frontend sends structured result/log event back (existing controller flow/bridge endpoint).
8. Backend forwards result into AI conversation context.
9. AI decides next action/fix.
10. User sees everything in one screen: action timeline + preview + logs.

---

## Build Tab UX Changes

1. Rename top-level tab label **Code → Build**.
2. Inside Build tab add switch:
   - **Visual** (default): preview + live logs + AI panel
   - **Code**: current splitter layout unchanged
3. Keep Console/Logs tabs for deep inspection; Visual mode shows compact live stream.
4. Keep full-screen preview button available.

---

## Backend Design Details

### Suggested Foldering

- `src/Automaton.Studio.Server/Services/Mcp/UiAutomation/`
  - `IUiAutomationTool.cs`
  - `UiAutomationToolRegistry.cs`
  - `UiAutomationOrchestrator.cs`
  - `UiAutomationSessionService.cs`
  - `Tools/UiClickTool.cs`, `Tools/UiTypeTool.cs`, ...
  - `Dtos/UiAutomationDtos.cs`

### Patterns

- Constructor DI for all dependencies.
- One class per tool.
- Static class with const strings for tool names and event types.
- Dedicated mapper for tool result DTO -> SSE payload.
- CancellationToken propagated through every async method.

---

## Frontend Design Details

### Suggested Foldering

- `src/automaton-studio-vue/src/features/apps/build/`
  - `components/BuildVisualWorkspace.vue`
  - `components/LiveUiLogPanel.vue`
  - `services/preview-automation-bridge.ts`
  - `services/preview-event-collector.ts`
  - `types/ui-automation.types.ts`

### Patterns

- Keep `AppEditorView.vue` as orchestrator; move heavy logic to services/components.
- Pure typed service methods for iframe interactions.
- No direct ad-hoc `postMessage` parsing in many places; central dispatcher.
- Use discriminated unions for event types.
- Reuse existing toast + error surfaces only for user-visible failures.

---

## Extensibility Strategy

To add a new UI MCP tool later:

1. Add one backend tool class implementing `IUiAutomationTool`.
2. Register in `UiAutomationToolRegistry`.
3. Add typed request/response in shared DTO/types.
4. Add bridge handler function in `preview-automation-bridge.ts`.
5. Add UI visualization mapping (icon/label/severity) once.

No changes required in conversation pipeline core.

---

## Validation Plan

### Functional

1. AI can click/type/read from preview app.
2. Console and browser errors appear in live log panel.
3. AI uses log feedback to apply file fixes.
4. Visual/Code mode switch keeps session/chat state.

### Quality

1. Frontend:
   - `npm run type-check`
   - `npm run lint`
2. Backend:
   - `dotnet build`
   - targeted tests for orchestrator/tool registry/tool handlers

### Non-Functional

1. No UI freeze during rapid event streams.
2. Bounded log memory (rolling window).
3. Clear failure paths with actionable errors.

---

## Delivery Phases

1. **Phase 1: Build UX shell**
   - Build tab rename, Visual/Code switch, visual workspace scaffold.
2. **Phase 2: Event capture**
   - Browser console/error capture to inline live logs.
3. **Phase 3: UI MCP v1**
   - click/type/navigate/read/assert/get logs.
4. **Phase 4: hardening**
   - retries, diagnostics metadata, tests, telemetry polish.

---

## Risks and Mitigations

1. **Cross-origin iframe constraints**
   - Keep preview served from same trusted origin path (`/app-preview/...`).
2. **Action flakiness (dynamic DOM)**
   - Standard wait primitives + explicit timeout results.
3. **Event noise**
   - Severity filtering + compact grouped rendering.
4. **Tool-result race conditions**
   - Correlation IDs and session-scoped action queueing.

---

## Definition of Done

1. User can watch AI building in real time in Build/Visual mode.
2. AI can interact with preview UI through MCP tools.
3. Live logs surface browser + automation issues inline.
4. AI can iteratively diagnose and fix based on those logs.
5. Code remains clean, modular, typed, and easy to extend with new UI MCP tools.

