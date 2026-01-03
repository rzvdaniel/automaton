# Token Usage Analysis: UnifiedAppMcpClient with Anthropic Claude

## Problem Statement

When creating a Dow Jones Index app using UnifiedAppMcpClient with Anthropic Claude, the system hit rate limits:
```
Error: Anthropic has rate limited your request
Rate limit: 30,000 input tokens per minute
```

This document analyzes what's being sent to the API and identifies optimization opportunities.

---

## Token Usage Breakdown

### 1. System Prompt (Sent with EVERY API call)

**Source**: `UnifiedAppSystemPromptBuilder.BuildSystemPrompt()`
**Location**: `src/Automaton.Studio/Services/Mcp/SystemPrompts/UnifiedAppSystemPromptBuilder.cs:22-42`

The system prompt concatenates 13 sections:

1. **GetWorkflowSection()** - Communication style, workflow patterns (~3KB)
2. **GetFrontendDesignStandards()** - Design requirements, Tailwind CSS (~2KB)
3. **GetToolSequenceSection()** - Tool workflow order (~1KB)
4. **GetHtmlTemplate()** - Complete HTML template example (~1.5KB)
5. **GetEmptyStateTemplate()** - Empty state HTML example (~0.5KB)
6. **GetBackendOutputRules()** - Data flow documentation (~2KB)
7. **pythonProvider.GetOutputVariableGuidance()** - Python output patterns (~1KB)
8. **pythonProvider.GetSystemMetricsExample()** - Full Python code example (~2KB)
9. **GetApiResponseStructure()** - API response format (~1.5KB)
10. **GetFrontendIntegrationPattern()** - Complete JavaScript example (~3KB)
11. **GetGoogleChartsIntegration()** - Google Charts example (~1KB)
12. **GetToolsAndNamingSection()** - Tool reference and conventions (~1.5KB)
13. **GetImportantRules()** - Final response format rules (~1KB)

**Estimated Total**: ~21KB text = **~5,250 tokens** (using 4 chars per token estimate)

### 2. Tool Definitions (Sent with EVERY API call)

**Source**: `UnifiedAppMcpToolProvider.GetToolsAsync()`
**Location**: `src/Automaton.Studio/Services/Mcp/ToolProviders/UnifiedAppMcpToolProvider.cs:86-278`

**Tools Included**:
1. `get_current_code` - Frontend code retrieval
2. `save_app_code` - Frontend code saving
3. `create_backend_step` - Backend step creation (auto-links)
4. `get_step_code` - Backend code retrieval
5. `save_step_code` - Backend code saving
6. `suggest_step_variables` - Variable definition
7. `execute_backend_step_code` - **NEW** - Testing tool
8. `link_step_to_app` - Link existing steps
9. `get_linked_steps` - View linked steps

Each tool includes:
- Name (string)
- Description (100-200 chars)
- InputSchema (JSON schema ~200-500 chars per tool)

**Estimated Total**: ~4-5KB = **~1,000-1,250 tokens**

### 3. Conversation History (Accumulates with each turn)

**Source**: `AnthropicProvider.BuildMessages()` + message accumulation loop
**Location**: `src/Automaton.Studio/Services/Mcp/Clients/Providers/AnthropicProvider.cs:156-182, 97-101`

**What accumulates**:
- User's initial message (~100-500 tokens)
- Assistant's text responses (varies, ~500-2000 tokens per turn)
- Tool use blocks (100-300 tokens per tool call)
- Tool result messages (varies by tool, 100-5000+ tokens)

**Example for Dow Jones app**:
```
Turn 1:
- User: "Create a Dow Jones Index app" (~20 tokens)
- Assistant: "I'll create..." (~200 tokens)
- Tool: create_backend_step (~50 tokens)
- Result: "Created step..." (~50 tokens)

Turn 2:
- Assistant: "Now defining variables..." (~100 tokens)
- Tool: suggest_step_variables (~200 tokens)
- Result: "Updated variables..." (~100 tokens)

Turn 3:
- Assistant: "Writing code..." (~300 tokens)
- Tool: save_step_code (~2000 tokens - includes full Python code)
- Result: "Code saved" (~50 tokens)

Turn 4:
- Assistant: "Testing step..." (~100 tokens)
- Tool: execute_backend_step_code (~100 tokens)
- Result: "Execution result..." (~500-1000 tokens - includes output, errors, etc.)

Turn 5:
- Assistant: "Fixing error..." (~200 tokens)
- Tool: save_step_code (~2000 tokens - fixed code)
- Result: "Code saved" (~50 tokens)

Turn 6:
- Assistant: "Re-testing..." (~50 tokens)
- Tool: execute_backend_step_code (~100 tokens)
- Result: "SUCCESS..." (~300 tokens)

Turn 7:
- Assistant: "Creating frontend..." (~500 tokens)
- Tool: save_app_code (~3000+ tokens - full HTML/CSS/JS)
- Result: "App saved" (~50 tokens)
```

**Total conversation**: ~10,000-12,000 tokens for a simple app with one backend step

### 4. Per-Turn Overhead

**AnthropicProvider implementation** (`AnthropicProvider.cs:50-132`):
- Each tool use iteration sends:
  - **Full system prompt** (5,250 tokens)
  - **All conversation history up to that point** (accumulates)
  - **All tool definitions** (1,000-1,250 tokens)
  - **New messages** (varies)

**Example token count for Turn 4 (testing)**:
```
System Prompt: 5,250 tokens
Tool Definitions: 1,000 tokens
Conversation History (Turns 1-3): ~4,000 tokens
New User Message: ~100 tokens
----------------------------------------
Total Input Tokens: ~10,350 tokens
```

---

## Token Usage Per Conversation

### Simple App (1 backend step, no errors)

**6-7 API calls**:
1. Create step (~6,500 tokens)
2. Define variables (~7,000 tokens)
3. Save code (~9,000 tokens)
4. Test code (~10,500 tokens)
5. Save frontend (~12,000 tokens)
6. Final response (~13,000 tokens)

**Total input tokens**: ~58,000 tokens
**Time to hit rate limit**: 30-40 seconds

### Complex App (multiple steps, with errors)

**10-15 API calls**:
- Create 2-3 backend steps
- Test-fix-retest cycles (2-3 iterations per step)
- Frontend integration
- Error handling and retries

**Total input tokens**: ~100,000-150,000 tokens
**Time to hit rate limit**: 15-20 seconds

### Dow Jones App (your example)

**Likely scenario**: 12-15 API calls
- Create "FetchDowJonesStep"
- Define variables (inputs/outputs)
- Save initial code
- **Test - ERROR (API endpoint issue)**
- Fix code
- **Re-test - ERROR (missing import)**
- Fix code again
- **Re-test - SUCCESS**
- Create frontend with chart visualization
- Final summary

**Estimated total**: ~120,000-150,000 input tokens
**Result**: **Rate limited after ~20 seconds**

---

## Why This Happens

### 1. System Prompt is Too Large (5,250 tokens)

**Issues**:
- Includes full HTML templates (unnecessary for every turn)
- Includes complete JavaScript code examples
- Includes Google Charts integration details (only needed if user asks)
- Includes Python code examples
- Sent with EVERY API call

**Example waste**: If you make 10 API calls, you send the system prompt 10 times = **52,500 tokens just for the system prompt**

### 2. Conversation History Sent Every Time

**Anthropic SDK behavior** (line 161-172):
```csharp
// Add conversation history
if (request.ConversationHistory?.Any() == true)
{
    foreach (var msg in request.ConversationHistory)
    {
        var role = msg.Role.ToLowerInvariant() == "assistant" ? RoleType.Assistant : RoleType.User;
        messages.Add(new Message
        {
            Role = role,
            Content = new List<ContentBase> { new TextContent { Text = msg.Content } }
        });
    }
}
```

**Problem**: Each turn includes ALL previous turns
- Turn 1: 0 history tokens
- Turn 2: 500 history tokens
- Turn 3: 1,500 history tokens
- Turn 4: 3,000 history tokens
- Turn 10: 15,000+ history tokens

### 3. Tool Results Can Be Large

**execute_backend_step_code** returns:
```json
{
  "success": true/false,
  "output": "logs...",
  "errors": ["..."],
  "errorInfo": {
    "errorType": "...",
    "message": "...",
    "lineNumber": X,
    "stackTrace": "..."  // Can be 1000+ characters
  },
  "outputVariables": {...},
  "recommendations": {...}
}
```

**This can be 500-2000 tokens per test**, and gets included in all subsequent API calls.

### 4. Code Saving Includes Full Code

**save_step_code** and **save_app_code** include complete code listings:
- Python step code: 500-2000 tokens
- Frontend code: 2000-5000+ tokens

These get stored in conversation history and re-sent on every subsequent turn.

---

## Rate Limit Calculation

**Anthropic Rate Limit**: 30,000 input tokens per minute

**Example Timeline**:
```
0:00 - Turn 1 (6,500 tokens) - Total: 6,500
0:03 - Turn 2 (7,000 tokens) - Total: 13,500
0:06 - Turn 3 (9,000 tokens) - Total: 22,500
0:09 - Turn 4 (10,500 tokens) - Total: 33,000 ❌ RATE LIMITED
```

**You hit the limit in ~9-15 seconds** for a complex app with testing iterations.

---

## Optimization Recommendations

### 1. HIGH PRIORITY: Reduce System Prompt Size

**Current**: ~5,250 tokens
**Target**: ~1,500-2,000 tokens

**Actions**:
- Remove code examples from system prompt (provide in docs instead)
- Remove HTML templates (AI knows HTML)
- Remove detailed library integration guides (Google Charts, etc.)
- Keep only:
  - Tool workflow order
  - Critical rules (testing required, auto-linking)
  - Data format rules (string serialization)
  - Naming conventions

**Estimated savings**: ~3,000-3,500 tokens per API call

### 2. HIGH PRIORITY: Implement Conversation Summarization

**Current**: Full history sent every time
**Target**: Summarize or truncate old turns

**Options**:
A. **Sliding window**: Keep only last 3-5 turns
B. **Summarization**: After 5 turns, summarize old turns into single message
C. **Smart truncation**: Keep only critical tool results (errors, final code)

**Estimated savings**: 2,000-5,000 tokens per API call after Turn 5

### 3. MEDIUM PRIORITY: Compress Tool Results

**Current**: Full execution results with stack traces
**Target**: Abbreviated results

**Actions**:
- For successful tests: Just return "✓ Test passed"
- For failed tests: Return only error type + line number (not full stack trace)
- Store full details in context, but don't include in conversation history

**Estimated savings**: 500-1,500 tokens per test

### 4. MEDIUM PRIORITY: Lazy-Load Tool Definitions

**Current**: All 9 tools sent every time
**Target**: Send only tools used in recent turns

**Challenges**: Anthropic SDK may require all tools upfront
**Alternative**: Group tools by context (frontend vs backend)

**Estimated savings**: 300-500 tokens per API call

### 5. LOW PRIORITY: Code Compression in History

**Current**: Full code listings stored in history
**Target**: Store hash or truncated version

**Actions**:
- For save_step_code: Store "<code saved - 150 lines Python>"
- For save_app_code: Store "<frontend saved - 300 lines HTML/JS>"
- Full code stored in context, but not re-sent to API

**Estimated savings**: 1,000-3,000 tokens per code save

---

## Implementation Priority

### Phase 1: Emergency Fix (30 minutes)
**Reduce system prompt by 50%**
- Remove code examples
- Remove HTML templates
- Keep only essential rules

**Impact**: Reduce tokens by ~3,000 per call
**Result**: 10 calls = 30,000 tokens saved (doubles your capacity)

### Phase 2: Conversation Management (2 hours)
**Implement sliding window**
- Keep only last 4-5 turns in history
- Summarize older turns

**Impact**: Reduce tokens by ~2,000-5,000 per call after Turn 5
**Result**: Can handle 15-20 call conversations before hitting limit

### Phase 3: Result Optimization (1 hour)
**Compress tool results**
- Abbreviate test results
- Remove unnecessary details from conversation history

**Impact**: Reduce tokens by ~500-1,000 per test
**Result**: Multiple test-fix-retest cycles possible

---

## Recommended Immediate Actions

1. **Reduce system prompt** (30 minutes)
   - File: `UnifiedAppSystemPromptBuilder.cs`
   - Remove: GetHtmlTemplate, code examples, chart integrations
   - Keep: Tool workflow, critical testing rules, data formats

2. **Add conversation history limit** (30 minutes)
   - File: `AnthropicProvider.cs`
   - Modify: BuildMessages() to keep only last 5 turns
   - Add: Summary of earlier turns

3. **Compress successful test results** (15 minutes)
   - File: `UnifiedAppMcpToolProvider.cs`
   - Modify: HandleExecuteBackendStepCode to return abbreviated success
   - Keep: Full error details for failures

**Total effort**: ~1.5 hours
**Expected result**: **3-4x increase in conversation capacity before rate limiting**

---

## Monitoring Recommendations

Add token counting to track usage:

```csharp
private int EstimateTokenCount(string text)
{
    // Rough estimate: 1 token ≈ 4 characters
    return text.Length / 4;
}

// In AnthropicProvider.CallAsync:
var systemPromptTokens = EstimateTokenCount(request.SystemPrompt);
var historyTokens = messages.Sum(m => EstimateTokenCount(m.Content));
var toolsTokens = anthropicTools.Sum(t => EstimateTokenCount(t.ToString()));

Logger.LogInformation("Token estimate - System: {System}, History: {History}, Tools: {Tools}, Total: {Total}",
    systemPromptTokens, historyTokens, toolsTokens, systemPromptTokens + historyTokens + toolsTokens);
```

This helps identify which calls are consuming the most tokens.

---

## Alternative: Use Claude Opus 4 with Higher Limits

**Current**: Claude 3.5 Sonnet (30,000 tokens/min)
**Alternative**: Claude Opus 4 or 4.5 (higher rate limits for paid tiers)

**Note**: This doesn't solve the inefficiency, just increases capacity. The optimizations above should still be implemented.

---

## Summary

**Current State**:
- System prompt: 5,250 tokens (sent 10+ times)
- Full conversation history: Accumulates rapidly
- Tool results: Detailed but verbose
- **Total**: Easily exceeds 30K tokens/min for complex apps

**After Optimizations**:
- System prompt: ~1,500 tokens (70% reduction)
- Conversation history: Last 5 turns only
- Tool results: Abbreviated
- **Total**: ~3-4x more capacity before rate limiting

**Implementation effort**: ~1.5-3 hours
**Impact**: **Enables complex multi-step apps without rate limiting**
