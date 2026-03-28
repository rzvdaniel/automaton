# IterationLimits vs MaxConversationHistoryMessages

This note explains two different limits in AI chat execution that are easy to confuse.

## Quick Summary

- `IterationLimits` controls how many LLM round-trips are allowed in one request flow.
- `MaxConversationHistoryMessages` controls how many previous chat messages are included as context in each call.

They affect different parts of behavior and should be tuned for different symptoms.

## 1) IterationLimits

What it controls:
- The maximum number of provider loop iterations (LLM call -> optional tool calls -> next LLM call).

Why it exists:
- Prevents runaway loops when tools keep getting called.
- Keeps cost and latency bounded.

Where in code:
- `src/Automaton.Mcp/SystemPrompts/IterationLimits.cs`
- `src/Automaton.Studio.Server/Controllers/AiConversationControllerBase.cs`

How config works:
- `AiProviders:MaxIterations = 0` means "use tier defaults".
- Tier defaults come from `AiProviders:IterationLimits` and built-in `IterationLimits` constants.

When to increase it:
- The model starts a task and stops before finishing multi-step tool work.
- Complex flows need more tool/LLM turns.

## 2) MaxConversationHistoryMessages

What it controls:
- The number of previous conversation messages included in each provider request.

Why it exists:
- Limits prompt growth and token usage.
- Higher values preserve more context for long conversations.

Where in code:
- `src/Automaton.Mcp/Providers/OpenAiCompatibleProvider.cs`
- `src/Automaton.Mcp/Providers/DeepInfraProvider.cs`
- Config model: `src/Automaton.Studio.Server/Config/AiProvidersConfig.cs`

Config key:
- `AiProviders:MaxConversationHistoryMessages`

When to increase it:
- The model forgets earlier context in long chats.
- Responses become inconsistent with prior messages.

## Practical Tuning Guidance

If problem is "stops in the middle":
- Increase `IterationLimits` (or `MaxIterations` override).

If problem is "forgets what we already discussed":
- Increase `MaxConversationHistoryMessages`.

If both happen:
- Increase both gradually and monitor token/cost impact.

Suggested starting point:
- `MaxConversationHistoryMessages`: 30
- Keep `MaxIterations: 0` and tune tier limits first.
