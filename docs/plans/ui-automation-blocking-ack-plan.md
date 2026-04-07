# UI Automation Blocking Ack Plan

## Goal
Make `ui_*` tools behave like real interactive browser control by requiring concrete client execution results in the same conversation turn, instead of placeholder-only envelopes.

## Answer to Architecture Question
This is not a separate system. It is an extension of the existing MCP tool pipeline plus the current App Editor preview bridge.

- Keep existing MCP `ui_*` tool names.
- Add a blocking acknowledgment protocol between backend and frontend.
- Feed concrete UI execution results back into the tool result path used by the model.

## Current Gap
Current flow returns a `clientExecutionRequired` envelope from MCP tools and frontend executes it locally, but the model does not reliably receive concrete execution results in the same turn.

## Target Flow
1. Model calls `ui_*` tool.
2. Backend emits tool-call event (already exists).
3. Frontend executes action immediately from tool-call args.
4. Frontend POSTs execution result by `toolCallId`.
5. Backend tool call blocks briefly waiting for that ack.
6. Backend returns the concrete UI result as the actual tool result to the model.
7. Model continues with verified runtime evidence.

## Phase 1 (Implement now)
- Add backend in-memory bridge service keyed by `toolCallId`.
- Add app conversation endpoint to receive UI tool reports.
- Add frontend API call to submit UI tool reports.
- Execute UI actions on tool-call reception (not only tool-result reception).
- Pass `toolCallId` through callback signatures.
- Keep backward compatibility with current `clientExecutionRequired` envelope.

## Phase 2 (Follow-up hardening)
- Persist recent UI reports per session for audit/diagnostics.
- Add stale/timeout cleanup metrics.
- Add explicit `ui_action_timeout` and `ui_action_missing_ack` error taxonomy.
- Add end-to-end tests for tool-call -> ack -> tool-result loop.

## Files in Phase 1
- `src/Automaton.Mcp/Tools/UiAutomationTool.cs`
- `src/Automaton.Mcp/Tools/IUiAutomationActionAckService.cs` (new)
- `src/Automaton.Mcp/Tools/UiAutomationActionAckService.cs` (new)
- `src/Automaton.Studio.Server/Program.cs`
- `src/Automaton.Studio.Server/Controllers/AiAppConversationController.cs`
- `src/automaton-studio-vue/src/shared/services/ai/ai-conversation-client.ts`
- `src/automaton-studio-vue/src/shared/composables/useAiChat.ts`
- `src/automaton-studio-vue/src/features/apps/components/AiAssistant.vue`
- `src/automaton-studio-vue/src/features/apps/views/AppEditorView.vue`

## Success Criteria
- `ui_*` tool results seen by model are concrete execution outcomes when ack arrives in time.
- Placeholder-only responses no longer represent successful verification.
- App Editor still works with existing UI logs and history views.
- No regressions in backend build and frontend type-check.
