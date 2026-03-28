using Automaton.Mcp.Models;
using Automaton.Mcp.SystemPrompts;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using Polly;
using Polly.Retry;
using System.ClientModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Mcp.Client.Providers;

/// <summary>
/// Provider for any OpenAI Chat Completions-compatible API (Moonshot, Groq, Together, Mistral, etc.).
/// Configure once per compatible provider — only <paramref name="providerName"/>, <paramref name="baseUrl"/>,
/// and <paramref name="defaultModel"/> differ between providers.
/// </summary>
public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private readonly ILogger _logger;
    private readonly string _baseUrl;

    private const int MaxConversationHistoryMessages = 10;

    public string ProviderName { get; }
    public string DefaultModel { get; }

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    /// <param name="providerName">Unique name matching AiProvider.Name in database (e.g. "Moonshot").</param>
    /// <param name="baseUrl">Base URL of the provider's OpenAI-compatible API (e.g. "https://api.moonshot.cn/v1").</param>
    /// <param name="defaultModel">Default model ID when ConversationRequest.Model is null.</param>
    /// <param name="logger">Logger instance.</param>
    public OpenAiCompatibleProvider(string providerName, string baseUrl, string defaultModel, ILogger logger)
    {
        ProviderName = providerName;
        DefaultModel = defaultModel;
        _baseUrl = baseUrl.TrimEnd('/');
        _logger = logger;
    }

    public async Task<ProviderResult> CallAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CallChatCompletionsAsync(request, tools, executeToolAsync, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("{Provider} conversation cancelled by client", ProviderName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling {Provider} API", ProviderName);
            throw;
        }
    }

    private async Task<ProviderResult> CallChatCompletionsAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrEmpty(request.Model) ? DefaultModel : request.Model;
        var apiKey = request.ApiKey ?? string.Empty;

        var client = BuildChatClient(model, apiKey);

        var toolList = tools.ToList();
        var chatTools = BuildChatTools(toolList);

        var messages = BuildMessages(request);

        var maxIterations = IterationLimits.Resolve(null, request.MaxIterations);
        var maxConsecutiveFailures = request.MaxConsecutiveFailures > 0 ? request.MaxConsecutiveFailures : 3;
        var iterationCount = 0;
        var consecutiveFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var responseBuilder = new StringBuilder();

        int? totalInputTokens = null;
        int totalOutputTokens = 0;
        int? totalCacheReadTokens = null;

        while (true)
        {
            iterationCount++;
            if (iterationCount > maxIterations)
            {
                _logger.LogWarning("{Provider} exceeded maximum iterations ({Max}), stopping", ProviderName, maxIterations);
                responseBuilder.AppendLine("\n\n[Conversation stopped: Maximum tool call iterations reached]");
                break;
            }

            var options = chatTools.Count > 0
                ? new ChatCompletionOptions { Tools = { } }
                : new ChatCompletionOptions();

            if (request.MaxTokens > 0)
                options.MaxOutputTokenCount = request.MaxTokens;

            if (chatTools.Count > 0)
            {
                foreach (var tool in chatTools)
                    options.Tools.Add(tool);
            }

            // Retry on rate limit with exponential backoff (same strategy as AnthropicProvider)
            var retryPipeline = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = request.MaxRateLimitRetries,
                    Delay             = TimeSpan.FromSeconds(request.RateLimitRetryInitialDelaySeconds),
                    BackoffType       = DelayBackoffType.Exponential,
                    UseJitter         = true,
                    ShouldHandle      = new PredicateBuilder().Handle<Exception>(IsRateLimitException),
                    DelayGenerator    = static args =>
                    {
                        if (TryParseRetryAfter(args.Outcome.Exception, out var retryAfter))
                            return ValueTask.FromResult<TimeSpan?>(retryAfter);
                        return ValueTask.FromResult<TimeSpan?>(null);
                    },
                    OnRetry           = args =>
                    {
                        var delaySecs = (int)args.RetryDelay.TotalSeconds;
                        _logger.LogWarning(
                            "{Provider} rate limit hit (attempt {Retry}/{MaxRetries}). Model={Model}, Retrying in {Delay}s. Reason: {Error}",
                            ProviderName, args.AttemptNumber + 1, request.MaxRateLimitRetries,
                            request.Model, delaySecs, args.Outcome.Exception?.Message);
                        EmitRetryNotification(request.ConversationId, args.AttemptNumber + 1, request.MaxRateLimitRetries, delaySecs);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();

            (string finalText, string reasoningText, List<ToolCallAccumulator> toolCalls, int? inputTokens, int? outputTokens, string? responseModel, int? cacheReadTokens)
                streamResult = default;
            await retryPipeline.ExecuteAsync(async ct =>
            {
                streamResult = await StreamOnceAsync(client, messages, options, request.ConversationId, ct);
            }, cancellationToken);

            // Accumulate tokens — input is cumulative (keep latest), output is additive, cache read is latest
            if (streamResult.inputTokens.HasValue) totalInputTokens = streamResult.inputTokens;
            if (streamResult.outputTokens.HasValue) totalOutputTokens += streamResult.outputTokens.Value;
            if (streamResult.cacheReadTokens.HasValue) totalCacheReadTokens = streamResult.cacheReadTokens;

            var finalText = streamResult.finalText;
            var reasoningText = streamResult.reasoningText;
            var toolCalls = streamResult.toolCalls;
            var responseModel = streamResult.responseModel ?? model;

            if (toolCalls.Count == 0)
            {
                // Final response — no more tool calls
                LogCacheStats(totalCacheReadTokens, totalInputTokens);
                if (!string.IsNullOrWhiteSpace(finalText))
                {
                    EmitStreamingText(request.ConversationId, finalText, isComplete: true,
                        inputTokens: totalInputTokens,
                        outputTokens: totalOutputTokens > 0 ? totalOutputTokens : null,
                        model: responseModel,
                        reasoningContent: string.IsNullOrEmpty(reasoningText) ? null : reasoningText,
                        cacheReadTokens: totalCacheReadTokens);
                    responseBuilder.AppendLine(finalText);
                }
                break;
            }

            // Emit partial text before executing tools
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                EmitStreamingText(request.ConversationId, finalText, isComplete: true,
                    inputTokens: totalInputTokens,
                    outputTokens: totalOutputTokens > 0 ? totalOutputTokens : null,
                    model: responseModel,
                    reasoningContent: string.IsNullOrEmpty(reasoningText) ? null : reasoningText,
                    cacheReadTokens: totalCacheReadTokens);
            }

            // Append assistant message (with tool calls) to conversation.
            // Thinking models (e.g. Moonshot kimi-k2.5) require reasoning_content to be echoed
            // back in the assistant message — the OpenAI SDK doesn't support this natively.
            // We build raw JSON and round-trip through ModelReaderWriter so the field is preserved
            // in SerializedAdditionalRawData and re-sent to the API on the next call.
            ChatMessage assistantMessage;
            if (!string.IsNullOrEmpty(reasoningText))
            {
                var toolCallNodes = toolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.FunctionName, arguments = tc.ArgumentsBuilder.ToString() }
                });
                var msgJson = JsonSerializer.Serialize(new
                {
                    role = "assistant",
                    reasoning_content = reasoningText,
                    content = string.IsNullOrWhiteSpace(finalText) ? "" : finalText,
                    tool_calls = toolCallNodes
                });
                assistantMessage = System.ClientModel.Primitives.ModelReaderWriter.Read<ChatMessage>(
                    BinaryData.FromString(msgJson),
                    System.ClientModel.Primitives.ModelReaderWriterOptions.Json)!;
            }
            else
            {
                var typed = new AssistantChatMessage(
                    toolCalls.Select(tc => ChatToolCall.CreateFunctionToolCall(
                        tc.Id, tc.FunctionName, BinaryData.FromString(tc.ArgumentsBuilder.ToString()))).ToList())
                {
                    Content = { }
                };
                if (!string.IsNullOrWhiteSpace(finalText))
                    typed.Content.Add(ChatMessageContentPart.CreateTextPart(finalText));
                assistantMessage = typed;
            }
            messages.Add(assistantMessage);

            var shouldStop = false;
            var shouldBreakForApproval = false;

            // Execute all tool calls concurrently
            var toolTasks = toolCalls.Select(async tc =>
            {
                var argsJson = tc.ArgumentsBuilder.ToString();
                if (string.IsNullOrEmpty(argsJson)) argsJson = "{}";

                EmitToolCallStarted(request.ConversationId, tc.Id, tc.FunctionName, argsJson);
                var result = await executeToolAsync(tc.Id, tc.FunctionName, argsJson);
                EmitToolCallCompleted(request.ConversationId, tc.Id, tc.FunctionName, argsJson, result);
                return (tc, argsJson, result);
            });

            var results = await Task.WhenAll(toolTasks);

            foreach (var (tc, argsJson, toolResult) in results)
            {
                var isFailure = toolResult.StartsWith("Tool execution failed:") ||
                                toolResult.StartsWith("Error executing tool") ||
                                toolResult.Contains("A second operation was started");

                if (isFailure)
                {
                    consecutiveFailures.TryGetValue(tc.FunctionName, out var currentCount);
                    consecutiveFailures[tc.FunctionName] = currentCount + 1;

                    if (consecutiveFailures[tc.FunctionName] >= maxConsecutiveFailures)
                    {
                        _logger.LogWarning("Tool {Tool} failed {Count} consecutive times, stopping", tc.FunctionName, consecutiveFailures[tc.FunctionName]);
                        shouldStop = true;
                    }
                }
                else
                {
                    consecutiveFailures[tc.FunctionName] = 0;
                }

                if (ProviderHelpers.ToolResultAwaitsApproval(tc.FunctionName, toolResult))
                    shouldBreakForApproval = true;

                messages.Add(new ToolChatMessage(tc.Id, toolResult));
            }

            if (shouldStop)
            {
                responseBuilder.AppendLine("\n\n[Conversation stopped: Tool repeatedly failed]");
                break;
            }

            if (shouldBreakForApproval)
                break;
        }

        return new ProviderResult(responseBuilder.ToString().Trim(), iterationCount);
    }

    private async Task<(string finalText, string reasoningText, List<ToolCallAccumulator> toolCalls, int? inputTokens, int? outputTokens, string? responseModel, int? cacheReadTokens)>
        StreamOnceAsync(ChatClient client, List<ChatMessage> messages, ChatCompletionOptions options,
            string conversationId, CancellationToken cancellationToken)
    {
        var currentText = new StringBuilder();
        var reasoningText = new StringBuilder();
        var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();
        int? inputTokens = null;
        int? outputTokens = null;
        int? cacheReadTokens = null;
        string? responseModel = null;
        var lastEmitTime = DateTime.UtcNow;
        const int emitIntervalMs = 100;

        var streamingUpdate = client.CompleteChatStreamingAsync(messages, options, cancellationToken);
        await foreach (var chunk in streamingUpdate)
        {
            // Extract non-standard fields (reasoning_content, cache tokens) from raw chunk JSON.
            // The OpenAI SDK ignores provider-specific usage fields, so we parse raw JSON directly.
            //
            // Cache field locations by provider:
            //   Kimi/Moonshot  → usage.prompt_cache_hit_tokens
            //   OpenAI         → usage.prompt_tokens_details.cached_tokens
            try
            {
                var rawBytes = System.ClientModel.Primitives.ModelReaderWriter.Write(chunk,
                    System.ClientModel.Primitives.ModelReaderWriterOptions.Json);
                using var doc = JsonDocument.Parse(rawBytes);
                var root = doc.RootElement;

                // reasoning_content — thinking models (e.g. kimi-k2.5)
                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("reasoning_content", out var rc) &&
                    rc.ValueKind == JsonValueKind.String)
                {
                    var rcText = rc.GetString();
                    if (!string.IsNullOrEmpty(rcText))
                        reasoningText.Append(rcText);
                }

                // Cache hit tokens — usage chunk (last chunk in stream)
                if (root.TryGetProperty("usage", out var usage))
                {
                    // Kimi/Moonshot: prompt_cache_hit_tokens
                    if (usage.TryGetProperty("prompt_cache_hit_tokens", out var kimiCache) &&
                        kimiCache.ValueKind == JsonValueKind.Number)
                        cacheReadTokens = kimiCache.GetInt32();

                    // OpenAI: prompt_tokens_details.cached_tokens
                    else if (usage.TryGetProperty("prompt_tokens_details", out var details) &&
                             details.TryGetProperty("cached_tokens", out var oaiCache) &&
                             oaiCache.ValueKind == JsonValueKind.Number)
                        cacheReadTokens = oaiCache.GetInt32();
                }
            }
            catch
            {
                // Non-standard fields absent in some providers — silently ignore
            }

            foreach (var part in chunk.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    currentText.Append(part.Text);
                    var now = DateTime.UtcNow;
                    if ((now - lastEmitTime).TotalMilliseconds >= emitIntervalMs)
                    {
                        EmitStreamingText(conversationId, currentText.ToString(), isComplete: false);
                        lastEmitTime = now;
                    }
                }
            }

            foreach (var toolUpdate in chunk.ToolCallUpdates)
            {
                if (!toolCallAccumulators.TryGetValue(toolUpdate.Index, out var acc))
                {
                    acc = new ToolCallAccumulator { Index = toolUpdate.Index };
                    toolCallAccumulators[toolUpdate.Index] = acc;
                }
                if (!string.IsNullOrEmpty(toolUpdate.ToolCallId))
                    acc.Id = toolUpdate.ToolCallId;
                if (!string.IsNullOrEmpty(toolUpdate.FunctionName))
                    acc.FunctionName = toolUpdate.FunctionName;
                if (toolUpdate.FunctionArgumentsUpdate != null)
                    acc.ArgumentsBuilder.Append(toolUpdate.FunctionArgumentsUpdate);
            }

            if (chunk.Usage != null)
            {
                inputTokens = chunk.Usage.InputTokenCount;
                outputTokens = chunk.Usage.OutputTokenCount;
            }
            if (chunk.Model != null)
                responseModel = chunk.Model;
        }

        return (currentText.ToString(), reasoningText.ToString(),
                toolCallAccumulators.Values.OrderBy(t => t.Index).ToList(),
                inputTokens, outputTokens, responseModel, cacheReadTokens);
    }

    private static bool IsRateLimitException(Exception ex)
    {
        var msg = ex.Message;
        // Exclude quota/billing errors — those are permanent and should not be retried.
        if (msg.Contains("insufficient_quota",       StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("quota_exceeded",           StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("exceeded_current_quota",   StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("insufficient balance",     StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("suspended",                StringComparison.OrdinalIgnoreCase))
            return false;

        return msg.Contains("rate_limit",      StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("rate limited",    StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("429",             StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseRetryAfter(Exception? ex, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (ex is null) return false;
        var match = RetryAfterPattern.Match(ex.Message);
        if (match.Success && int.TryParse(match.Groups["secs"].Value, out var secs) && secs > 0)
        {
            retryAfter = TimeSpan.FromSeconds(secs);
            return true;
        }
        return false;
    }

    private static readonly Regex RetryAfterPattern = new(
        @"retry[- ](?:after|in)\D*(?<secs>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private void LogCacheStats(int? cacheReadTokens, int? totalInputTokens)
    {
        if (cacheReadTokens > 0)
            _logger.LogDebug(
                "{Provider} cache hit — read: {CacheRead} tokens (saved ~{Saved} tokens from billing)",
                ProviderName, cacheReadTokens, cacheReadTokens);
        else
            _logger.LogDebug("{Provider} cache stats — no cache hit this request (input: {Input} tokens)",
                ProviderName, totalInputTokens ?? 0);
    }

    private void EmitRetryNotification(string conversationId, int attempt, int maxRetries, int delaySeconds)
    {
        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [new ConversationMessage("assistant",
                $"⏳ Rate limit reached. Retrying in {delaySeconds}s (attempt {attempt}/{maxRetries})...")],
            ToolCalls = [],
            ToolResults = []
        });
    }

    private ChatClient BuildChatClient(string model, string apiKey)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri($"{_baseUrl}/")
        };
        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        return openAiClient.GetChatClient(model);
    }

    private static List<ChatTool> BuildChatTools(List<ProviderTool> tools)
    {
        var chatTools = new List<ChatTool>();
        foreach (var tool in tools)
        {
            try
            {
                var schema = BinaryData.FromString(tool.InputSchemaJson);
                chatTools.Add(ChatTool.CreateFunctionTool(tool.Name, tool.Description, schema));
            }
            catch (Exception)
            {
                // Skip malformed tool schemas
            }
        }
        return chatTools;
    }

    private static List<ChatMessage> BuildMessages(ConversationRequest request)
    {
        var messages = new List<ChatMessage>();

        // Split system prompt into separate messages so OpenAI's automatic prefix caching
        // can hit on the stable BasePrompt. Sending them as one concatenated string would
        // invalidate the cache on every request because InstructionBlock/MemoryBlock change.
        if (request.SystemPromptParts != null)
        {
            messages.Add(new SystemChatMessage(request.SystemPromptParts.BasePrompt));
            if (!string.IsNullOrWhiteSpace(request.SystemPromptParts.InstructionBlock))
                messages.Add(new SystemChatMessage(request.SystemPromptParts.InstructionBlock));
            if (!string.IsNullOrWhiteSpace(request.SystemPromptParts.MemoryBlock))
                messages.Add(new SystemChatMessage(request.SystemPromptParts.MemoryBlock));
        }
        else if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new SystemChatMessage(request.SystemPrompt));
        }

        if (request.ConversationHistory?.Count > 0)
        {
            var history = request.ConversationHistory;
            if (history.Count > MaxConversationHistoryMessages)
                history = history.Skip(history.Count - MaxConversationHistoryMessages).ToList();

            foreach (var msg in history)
            {
                messages.Add(msg.Role.ToLowerInvariant() switch
                {
                    "assistant" => new AssistantChatMessage(msg.Content),
                    "system" => new SystemChatMessage(msg.Content),
                    _ => (ChatMessage)new UserChatMessage(msg.Content)
                });
            }
        }

        messages.Add(new UserChatMessage(request.InitialMessage));
        return messages;
    }

    private void EmitStreamingText(string conversationId, string text, bool isComplete,
        int? inputTokens = null, int? outputTokens = null, string? model = null,
        string? reasoningContent = null, int? cacheReadTokens = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        var message = new ConversationMessage("assistant", text)
        {
            IsStreaming = !isComplete,
            InputTokens = isComplete ? inputTokens : null,
            OutputTokens = isComplete ? outputTokens : null,
            Model = isComplete ? model : null,
            ReasoningContent = isComplete ? reasoningContent : null,
            CacheReadTokens = isComplete ? cacheReadTokens : null
        };

        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [message],
            ToolCalls = [],
            ToolResults = []
        });
    }

    private void EmitToolCallStarted(string conversationId, string toolCallId, string toolName, string argsJson)
    {
        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [],
            ToolCalls = [new ToolCall { Id = toolCallId, Name = toolName, Arguments = ParseArgs(argsJson) }],
            ToolResults = []
        });
    }

    private void EmitToolCallCompleted(string conversationId, string toolCallId, string toolName, string argsJson, string result)
    {
        var isSuccess = !result.StartsWith("Tool execution failed:") && !result.StartsWith("Error executing tool");
        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [],
            ToolCalls = [new ToolCall { Id = toolCallId, Name = toolName, Arguments = ParseArgs(argsJson) }],
            ToolResults = [new ToolResult { ToolCallId = toolCallId, Result = result, IsSuccess = isSuccess, ErrorMessage = isSuccess ? null : result }]
        });
    }

    private static Dictionary<string, object?> ParseArgs(string argsJson)
    {
        try
        {
            if (string.IsNullOrEmpty(argsJson) || argsJson == "{}")
                return [];
            return JsonDocument.Parse(argsJson).RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => (object?)p.Value.ToString());
        }
        catch
        {
            return [];
        }
    }

    private sealed class ToolCallAccumulator
    {
        public int Index { get; set; }
        public string Id { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
        public StringBuilder ArgumentsBuilder { get; } = new();
    }
}
