# AI Reasoning and Communication in Automaton Studio

## Overview

This document describes how the AI Chat Panel displays AI activity, reasoning, and tool execution feedback to users. It covers the architecture, message types, and system prompt improvements made to enhance user visibility into AI operations.

---

## Table of Contents

1. [Problem Statement](#problem-statement)
2. [Architecture Overview](#architecture-overview)
3. [Message Types](#message-types)
4. [How AI Messages Flow](#how-ai-messages-flow)
5. [System Prompt Communication Style](#system-prompt-communication-style)
6. [Components](#components)
7. [Best Practices from Industry Tools](#best-practices-from-industry-tools)
8. [Troubleshooting](#troubleshooting)

---

## Problem Statement

### Original Issues

1. **Silent AI Operations**: Users saw long gaps (30s-60s) between visible updates while the AI was working
2. **No Intermediate Feedback**: Tool calls appeared, but there was no explanation of what the AI was doing or thinking
3. **Brittle Model Detection**: Code tried to detect "reasoning models" (o1, o3) by hardcoded name patterns
4. **Inconsistent UX**: Different models showed different levels of feedback

### Goals

- Show users what the AI is doing in real-time
- Display intermediate thoughts/explanations between tool calls
- Provide consistent UX across all AI models
- Remove fragile model name detection

---

## Architecture Overview

```
???????????????????????????????????????????????????????????????????
?                        AiChatPanel.razor                         ?
?  ??????????????????????????????????????????????????????????????? ?
?  ?                    Message Display Loop                      ? ?
?  ?  - User messages                                             ? ?
?  ?  - AI Activity messages (collapsible)                        ? ?
?  ?  - Tool Execution messages (with status)                     ? ?
?  ?  - Code Success/Error messages                               ? ?
?  ??????????????????????????????????????????????????????????????? ?
???????????????????????????????????????????????????????????????????
                              ?
                              ? subscribes to
                              ?
???????????????????????????????????????????????????????????????????
?                    IMcpClient.ConversationUpdated               ?
?  Events fired for:                                               ?
?  - AI text messages (thinking, explanations)                     ?
?  - Tool calls (started)                                          ?
?  - Tool results (completed)                                      ?
???????????????????????????????????????????????????????????????????
                              ?
                              ? fires events from
                              ?
???????????????????????????????????????????????????????????????????
?                     AutomatonMcpClient.cs                        ?
?  - Calls OpenAI/Anthropic/Google APIs                           ?
?  - Processes tool calls                                          ?
?  - Emits ConversationUpdated events in real-time                ?
???????????????????????????????????????????????????????????????????
```

---

## Message Types

Defined in `MessageType` enum:

| Type | Description | UI Component |
|------|-------------|--------------|
| `System` | System notifications | SystemMessageComponent |
| `Text` | Regular AI/User text | TextMessageComponent |
| `Reasoning` | AI intermediate thoughts (collapsible) | ReasoningMessageComponent |
| `ToolExecution` | Tool calls with status | ToolExecutionMessageComponent |
| `CodeSuccess` | Successfully generated code | CodeSuccessMessageComponent |
| `CodeError` | Error messages | CodeErrorMessageComponent |
| `Generating` | "AI is working..." indicator | GeneratingMessageComponent |

### When to Use Each Type

```csharp
// During generation (isGeneratingCode = true):
// - Intermediate AI messages ? MessageType.Reasoning (collapsible)
// - Tool calls ? MessageType.ToolExecution

// After generation (isGeneratingCode = false):
// - Final AI responses ? MessageType.Text
// - Code with Apply button ? MessageType.CodeSuccess
```

---

## How AI Messages Flow

### 1. User Sends Prompt

```csharp
private async Task SendPrompt()
{
    await BeginConversation();           // Sets isGeneratingCode = true
    var context = await PrepareConversationContext();
    var result = await ExecuteConversation(context);
    await ProcessConversationResult(result, context.UserPrompt);
    await EndConversation();             // Sets isGeneratingCode = false
}
```

### 2. MCP Client Fires Events

The `AutomatonMcpClient` fires `ConversationUpdated` events at these points:

```csharp
// In CallOpenAiAsync method:

// When AI provides text alongside tool calls (thinking/reasoning):
if (!string.IsNullOrWhiteSpace(thinkingText))
{
    ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
    {
        ConversationId = request.ConversationId,
        Messages = new List<ConversationMessage> { new("assistant", thinkingText) },
        ToolCalls = new List<ToolCall>(),
        ToolResults = new List<ToolResult>()
    });
}

// When AI requests a tool call:
ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
{
    ConversationId = request.ConversationId,
    Messages = new List<ConversationMessage>(),
    ToolCalls = currentToolCalls,
    ToolResults = new List<ToolResult>()
});

// When tool execution completes:
ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
{
    ConversationId = request.ConversationId,
    Messages = new List<ConversationMessage>(),
    ToolCalls = currentToolCalls,
    ToolResults = new List<ToolResult> { toolResultObj }
});

// When AI provides final response:
ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
{
    ConversationId = request.ConversationId,
    Messages = new List<ConversationMessage> { new("assistant", assistantText) },
    ToolCalls = new List<ToolCall>(),
    ToolResults = new List<ToolResult>()
});
```

### 3. AiChatPanel Processes Events

```csharp
private void OnConversationUpdated(object? sender, ConversationUpdatedEvent e)
{
    if (e.ConversationId == _conversationId)
    {
        InvokeAsync(() =>
        {
            // Process AI messages
            foreach (var message in e.Messages)
            {
                if (message.Role == "assistant" && !string.IsNullOrWhiteSpace(message.Content))
                {
                    // Skip duplicates (exact or similar)
                    if (IsExactOrSimilarDuplicate(message.Content)) continue;
                    
                    // Determine message type based on context
                    var isIntermediate = isGeneratingCode && IsIntermediateAiMessage(message.Content);
                    var messageType = isIntermediate ? MessageType.Reasoning : MessageType.Text;
                    
                    messages.Add(new ChatMessage("AI", message.Content, DateTime.Now)
                    {
                        Type = messageType,
                        IsReasoningCollapsed = false
                    });
                }
            }
            
            // Process tool calls (show "executing" status)
            foreach (var toolCall in e.ToolCalls) { /* ... */ }
            
            // Process tool results (update to "success"/"error")
            foreach (var toolResult in e.ToolResults) { /* ... */ }
            
            StateHasChanged();
            ScrollToBottom();
        });
    }
}
```

### 4. Intermediate vs Final Messages

```csharp
private bool IsIntermediateAiMessage(string content)
{
    // Very short messages are not meaningful
    if (content.Length < 10) return false;
    
    // Pure code blocks are final responses, not intermediate
    if (IsPureCodeBlock(content)) return false;
    
    // Everything else during generation is intermediate
    return true;
}
```

### 5. Auto-Collapse After Generation

```csharp
private void CollapseReasoningMessages()
{
    messages = messages.Select(msg =>
    {
        if (msg.Type == MessageType.Reasoning && !msg.IsReasoningCollapsed)
        {
            return msg with { IsReasoningCollapsed = true };
        }
        return msg;
    }).ToList();
}
```

---

## System Prompt Communication Style

### The Problem

GPT models (GPT-4, GPT-4o, GPT-5) are task-focused and don't naturally "think aloud." They:
1. Receive request
2. Think internally (not visible)
3. Call tool
4. Think internally (not visible)
5. Call next tool
6. ...
7. Provide final answer

### The Solution

Add explicit instructions in system prompts to encourage communication:

```
## COMMUNICATION STYLE:
? **ALWAYS EXPLAIN WHAT YOU'RE DOING** ?

Before EACH tool call, briefly explain your plan:
- "I'll first read the existing code to understand the current implementation..."
- "Now I'll create a backend step for the API integration..."
- "The step is ready, let me save the updated frontend code..."

After each tool result, briefly acknowledge it:
- "Got the current code. I can see it's using Tailwind CSS..."
- "The step was created successfully. Now linking it to the app..."

This helps users understand your progress during longer operations.
```

### Where This Is Applied

1. **UnifiedAppSystemPromptBuilder.cs** - For full-stack app generation
2. **StepSystemPromptBuilder.cs** - For custom step code generation  
3. **AppSystemPromptBuilder.cs** - For frontend-only app generation

### Example Conversation After Improvement

```
User: Can we update the background with a warmer color?

AI: I'll read the current code first to see the existing styling...

?? Reading current code from editor ? Success
   View Code (318 lines)

AI: I see the current implementation uses a dark gray background. 
    Let me update it to a warm, newsprint-like cream color...

?? Save app code ? Success
   Code saved successfully to App Designer

AI: I updated the app's background to a warm, newsprint-like cream 
    color (#f5f0e6) which is reminiscent of The Economist's style.
```

---

## Components

### ReasoningMessageComponent.razor

Displays collapsible "AI Activity" messages:

```razor
<div class="reasoning-message">
    <div class="reasoning-header" @onclick="ToggleCollapsed">
        <span class="reasoning-icon">@(IsCollapsed ? "?" : "?")</span>
        <span class="reasoning-label">?? AI Activity</span>
        @if (!string.IsNullOrEmpty(Summary))
        {
            <span class="reasoning-summary">@Summary</span>
        }
    </div>
    
    @if (!IsCollapsed)
    {
        <div class="reasoning-content">
            @((MarkupString)FormattingService.FormatReasoningTextWithCode(Content))
        </div>
    }
</div>
```

Features:
- Collapsible with click
- Shows summary when collapsed
- Auto-generates summary from first line
- Supports code blocks within reasoning

### ToolExecutionMessageComponent.razor

Displays tool calls with status:

```
?? Reading current code from editor
   Status: ? Success | ? Error | ? Executing
   [View Code] [Raw Result]
```

---

## Best Practices from Industry Tools

### What VS Copilot Does

1. **Shows streaming responses** - Text appears as it's generated
2. **Shows tool execution in real-time** - Each tool call displayed immediately
3. **Doesn't detect "reasoning models"** - Shows ALL intermediate messages
4. **Uses progressive disclosure** - Collapsible sections for verbose output

### Key Insight

> **Show ALL intermediate AI communication, regardless of model type.**

The distinction between "reasoning models" (o1, o3) and regular models was artificial. All models can provide intermediate thoughts IF you ask them to in the system prompt.

### What We Removed

We removed this brittle model detection code:

```csharp
// ? REMOVED - Brittle and unnecessary
private bool IsReasoningModel()
{
    return provider.Provider switch
    {
        AiProviderType.OpenAI => IsOpenAiReasoningModel(lowerModel),
        AiProviderType.Google => IsGoogleReasoningModel(lowerModel),
        AiProviderType.Anthropic => IsAnthropicReasoningModel(lowerModel),
        _ => lowerModel.Contains("reasoning") || lowerModel.Contains("thinking")
    };
}

private bool IsOpenAiReasoningModel(string modelName)
{
    return modelName.StartsWith("o1") || 
           modelName.StartsWith("o3") || 
           modelName.StartsWith("o4");
}
```

### What We Replaced It With

Simple context-based detection:

```csharp
// ? CURRENT - Based on context, not model name
var isIntermediateMessage = isGeneratingCode && IsIntermediateAiMessage(message.Content);
var messageType = isIntermediateMessage ? MessageType.Reasoning : MessageType.Text;
```

---

## Troubleshooting

### Problem: Still Seeing Long Gaps

**Possible Causes:**
1. AI not following the "COMMUNICATION STYLE" prompt instructions
2. Model doesn't emit text alongside tool calls
3. Events not being fired from MCP client

**Solutions:**
1. Make the prompt instructions more explicit
2. Test with different models (Claude tends to be more verbose)
3. Add logging to verify events are firing

### Problem: Duplicate Messages

**Cause:** Same message being added multiple times from different event sources

**Solution:** The duplicate detection in `OnConversationUpdated`:

```csharp
// Exact duplicate check
var isExactDuplicate = messages.Any(m => 
    m.Role == "AI" && 
    (m.Type == MessageType.Text || m.Type == MessageType.Reasoning) &&
    m.Content.Trim().Equals(normalizedNewContent, StringComparison.Ordinal));

// Similar duplicate check (95% similarity threshold)
var isSimilarDuplicate = messages.Any(m => 
    m.Role == "AI" && 
    (m.Type == MessageType.Text || m.Type == MessageType.Reasoning) &&
    TextSimilarityService.AreSimilar(m.Content, message.Content, 0.95));
```

### Problem: Reasoning Not Collapsing

**Cause:** `EndConversation()` not being called or `CollapseReasoningMessages()` failing

**Solution:** Ensure the finally block runs:

```csharp
try
{
    // ... conversation logic
}
finally
{
    await EndConversation(); // This calls CollapseReasoningMessages()
}
```

---

## Related Files

| File | Purpose |
|------|---------|
| `AiChatPanel.razor.cs` | Main chat panel logic, event handling |
| `AiChatPanel.razor` | Chat panel UI template |
| `ChatMessageComponent.razor` | Routes messages to appropriate component |
| `ReasoningMessageComponent.razor` | Collapsible AI activity display |
| `ToolExecutionMessageComponent.razor` | Tool call status display |
| `UnifiedAppSystemPromptBuilder.cs` | Full-stack app system prompt |
| `StepSystemPromptBuilder.cs` | Custom step system prompt |
| `AppSystemPromptBuilder.cs` | Frontend app system prompt |
| `AutomatonMcpClient.cs` | MCP client, fires ConversationUpdated events |
| `ChatMessage.cs` | Message model with Type, Content, etc. |
| `MessageType.cs` | Enum for message types |

---

## Future Improvements

1. **Streaming Support**: Display AI text character-by-character as it arrives
2. **Progress Indicators**: Show elapsed time for long operations
3. **Model Capabilities API**: Let providers report capabilities instead of guessing
4. **Heartbeat Messages**: Show "Still working..." for operations > 30s
5. **Token Usage Display**: Show token consumption for transparency

---

## Changelog

### 2025-01-XX - Initial Implementation

- Removed brittle `IsReasoningModel()` detection
- Simplified message type logic to context-based detection
- Added "COMMUNICATION STYLE" section to all system prompts
- Renamed "?? AI Reasoning" to "?? AI Activity" in UI
- Updated `ReasoningMessageComponent` styling
- Added comprehensive duplicate detection
- Added auto-collapse for reasoning messages after generation
