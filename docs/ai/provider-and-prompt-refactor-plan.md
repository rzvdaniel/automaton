# Provider and Prompt Refactor Plan

## 1. Industry Standard (Edits/Reads/Tooling)

### Goals
- Minimize round-trips and token usage while preserving correctness.
- Separate discovery from mutation.
- Make file edits deterministic and easy to audit.

### Standard Workflow
1. Discovery pass:
   - Use search/symbol tools to locate targets.
   - Read only required file ranges.
2. Mutation pass:
   - For 1-2 isolated edits in one file, use targeted edit operations.
   - For 3+ edits or structural changes, perform read-once then full write.
3. Verification pass:
   - Run focused validation (build/tests) once edits are complete.

### Operational Guardrails
- Treat every tool call as a paid API round-trip.
- Batch independent tool calls in one response.
- Avoid repeated search/edit loops on the same file.
- Do not read files immediately after writing unless validation requires it.

### Token Accounting Standard
- Track billed usage as additive totals across provider calls.
- Emit final turn totals once per completed assistant turn.
- Keep intermediate streaming updates free of additive billing totals to avoid double counting.

## 2. How I Would Refactor Your Providers

### Current Refactor Scope
- Files:
  - src/Automaton.Mcp/Providers/OpenAiCompatibleProvider.cs
  - src/Automaton.Mcp/Providers/OpenAiProvider.cs
  - src/Automaton.Mcp/Providers/AnthropicProvider.cs
  - src/Automaton.Mcp/Providers/GoogleProvider.cs

### Refactor Decisions
1. Additive totals across iterations:
   - Input/output/cache token values are accumulated per provider API call.
2. Final-only billing emission:
   - Intermediate updates carry no additive token totals.
   - Final update carries the aggregate totals.
3. Token-only final event support:
   - If the final turn has no assistant text, still emit a complete token event so persistence captures billing.

### Why This Matches Industry Practice
- Prevents undercounting when a run has many tool iterations.
- Prevents overcounting from repeated intermediate emissions.
- Preserves real-time streaming UX while keeping persisted accounting accurate.

## 3. How I Would Refactor Base Prompts

### Current Refactor Scope
- File:
  - src/Automaton.Mcp/SystemPrompts/AppPrompts/app-base.md

### Refactor Decisions
1. Explicit decision matrix:
   - New file -> write_file
   - 1-2 isolated edits -> search_file + edit_file
   - 3+ edits or structural rewrite -> read_file + write_file
2. Anti-loop policy:
   - After 2 repeated search/edit cycles in the same file, switch to read/write.
3. Tool budget framing:
   - Require batching independent operations.
   - Prefer fewer larger operations over many tiny calls.

### Why This Matches Industry Practice
- Converts vague guidance into deterministic execution rules.
- Reduces tool-call chatter and token burn.
- Improves consistency across runs and models.

## Validation Checklist
- Provider project builds cleanly after changes.
- Final turn usage persists even for token-only final turns.
- Prompt guidance clearly enforces write-over-edit for multi-change files.
- Prompt guidance includes explicit anti-loop behavior.
