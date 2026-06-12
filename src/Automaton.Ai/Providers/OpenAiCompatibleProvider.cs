using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using Polly;
using Polly.Retry;
using System.ClientModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Ai.Providers;

/// <summary>
/// Provider for any OpenAI Chat Completions-compatible API (Moonshot, Groq, Together, Mistral, etc.).
/// Configure once per compatible provider — only <paramref name="providerName"/>, <paramref name="baseUrl"/>,
/// and <paramref name="defaultModel"/> differ between providers.
/// </summary>
public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private readonly ILogger _logger;
    private readonly string _baseUrl;
    private readonly int _maxConversationHistoryMessages;

    public string ProviderName { get; }
    public string DefaultModel { get; }

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    /// <param name="providerName">Unique name matching AiProvider.Name in database (e.g. "Moonshot").</param>
    /// <param name="baseUrl">Base URL of the provider's OpenAI-compatible API (e.g. "https://api.moonshot.cn/v1").</param>
    /// <param name="defaultModel">Default model ID when ConversationRequest.Model is null.</param>
    /// <param name="logger">Logger instance.</param>
    public OpenAiCompatibleProvider(
        string providerName,
        string baseUrl,
        string defaultModel,
        ILogger logger,
        int maxConversationHistoryMessages = 30)
    {
        ProviderName = providerName;
        DefaultModel = defaultModel;
        _baseUrl = baseUrl.TrimEnd('/');
        _logger = logger;
        _maxConversationHistoryMessages = maxConversationHistoryMessages > 0 ? maxConversationHistoryMessages : 30;
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
        catch (ClientResultException crex)
        {
            // Extract the full response body from the raw HTTP response so the actual API error
            // (e.g. "model not found", "tools not supported") is visible in logs.
            // crex.Message is only the SDK generic string; the real detail is in the response body.
            string? rawBody = null;
            try { rawBody = crex.GetRawResponse()?.Content.ToString(); } catch { /* ignore */ }
            _logger.LogError(
                "{Provider} API error {Status} for model '{Model}'. KeyLength={KeyLen}. Body: {Body}",
                ProviderName,
                crex.Status,
                request.Model,
                request.ApiKey?.Length ?? 0,
                rawBody ?? crex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Provider} Error calling {Provider} API for model '{Model}'", ProviderName, ProviderName, request.Model);
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

        var client = BuildChatClient(model, apiKey, request.ExtraBodyJson);

        var toolList = tools.ToList();
        var chatTools = BuildChatTools(toolList);

        var messages = BuildMessages(request);

        var maxIterations = ProviderIterationLimits.Resolve(request.MaxIterations);
        var maxConsecutiveFailures = request.MaxConsecutiveFailures > 0 ? request.MaxConsecutiveFailures : 3;
        var iterationCount = 0;
        var consecutiveFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var responseBuilder = new StringBuilder();

        var tokenUsage = new TokenUsageAccumulator();

        // When propose_plan returns awaitingApproval, suppress tools on the next iteration
        // so the LLM can present the plan and ask for user confirmation.
        var breakAfterPlanPresentation = false;

        while (true)
        {
            iterationCount++;
            if (iterationCount > maxIterations)
            {
                _logger.LogWarning("{Provider} exceeded maximum iterations ({Max}), stopping", ProviderName, maxIterations);
                responseBuilder.AppendLine("\n\n[Conversation stopped: Maximum tool call iterations reached]");
                break;
            }

            var includeTools = request.SupportsTools && chatTools.Count > 0 && !breakAfterPlanPresentation;

            var options = includeTools
                ? new ChatCompletionOptions { Tools = { } }
                : new ChatCompletionOptions();

            if (request.MaxTokens > 0)
                options.MaxOutputTokenCount = request.MaxTokens;
            if (request.Temperature.HasValue)
                options.Temperature = request.Temperature.Value;
            if (request.TopP.HasValue)
                options.TopP = request.TopP.Value;

            if (includeTools)
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

            (string finalText, string reasoningText, string? reasoningFieldName, List<ToolCallAccumulator> toolCalls, int? inputTokens, int? outputTokens, string? responseModel, int? cacheReadTokens)
                streamResult = (string.Empty, string.Empty, null, new List<ToolCallAccumulator>(), null, null, null, null);
            await retryPipeline.ExecuteAsync(async ct =>
            {
                streamResult = await StreamOnceAsync(client, messages, options, request.ConversationId, request.StreamingTimeoutMinutes, ct);
            }, cancellationToken);

            // Accumulate tokens across all iterations — input, output, and cache are all per-call values.
            // Tokens are only emitted once on the final turn so the server accumulates the full billing total.
            tokenUsage.Add(streamResult.inputTokens, streamResult.outputTokens, cacheReadTokens: streamResult.cacheReadTokens);

            var finalText = streamResult.finalText;
            var reasoningText = streamResult.reasoningText;
            var reasoningFieldName = streamResult.reasoningFieldName;
            var toolCalls = streamResult.toolCalls;
            var responseModel = streamResult.responseModel ?? model;

            if (toolCalls.Count == 0)
            {
                // Final response — no more tool calls
                LogCacheStats(tokenUsage.CacheReadTokens, tokenUsage.InputTokens);
                if (!string.IsNullOrWhiteSpace(finalText))
                {
                    EmitStreamingText(request.ConversationId, finalText, isComplete: true,
                        inputTokens: tokenUsage.InputTokens,
                        outputTokens: tokenUsage.OutputTokensOrNull,
                        model: responseModel,
                        reasoningContent: string.IsNullOrEmpty(reasoningText) ? null : reasoningText,
                        cacheReadTokens: tokenUsage.CacheReadTokens);
                    responseBuilder.AppendLine(finalText);
                }
                else if (tokenUsage.HasAny)
                {
                    EmitStreamingText(request.ConversationId, string.Empty, isComplete: true,
                        inputTokens: tokenUsage.InputTokens,
                        outputTokens: tokenUsage.OutputTokensOrNull,
                        model: responseModel,
                        reasoningContent: string.IsNullOrEmpty(reasoningText) ? null : reasoningText,
                        cacheReadTokens: tokenUsage.CacheReadTokens);
                }
                break;
            }

            // Emit partial text before executing tools.
            // Tokens are intentionally omitted here — they are reported only on the final turn so
            // the server's += accumulator receives the complete billing total exactly once.
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                EmitStreamingText(request.ConversationId, finalText, isComplete: true,
                    inputTokens: null,
                    outputTokens: null,
                    model: responseModel,
                    reasoningContent: string.IsNullOrEmpty(reasoningText) ? null : reasoningText,
                    cacheReadTokens: null);
            }

            // Append assistant message (with tool calls) to conversation.
            // Thinking models require the reasoning field to be echoed back — field name and format differ by provider:
            //   Kimi / Moonshot: "reasoning_content": "<text>"
            //   OpenRouter:      "reasoning_details": [{"type":"thinking","thinking":"<text>"}]
            // The OpenAI SDK doesn't support these natively, so we build raw JSON and round-trip through
            // ModelReaderWriter so the field is preserved in SerializedAdditionalRawData and re-sent to the API.
            ChatMessage assistantMessage;
            if (!string.IsNullOrEmpty(reasoningText))
            {
                var toolCallNodes = toolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.FunctionName, arguments = tc.ArgumentsBuilder.ToString() }
                });
                string msgJson;
                if (reasoningFieldName == "reasoning")
                {
                    // OpenRouter: reasoning_details is an array of typed thinking blocks
                    var reasoningDetails = new[]
                    {
                        new Dictionary<string, string> { ["type"] = "thinking", ["thinking"] = reasoningText }
                    };
                    msgJson = JsonSerializer.Serialize(new
                    {
                        role = "assistant",
                        reasoning_details = reasoningDetails,
                        content = string.IsNullOrWhiteSpace(finalText) ? "" : finalText,
                        tool_calls = toolCallNodes
                    });
                }
                else
                {
                    // Kimi / Moonshot: reasoning_content is a plain string
                    msgJson = JsonSerializer.Serialize(new
                    {
                        role = "assistant",
                        reasoning_content = reasoningText,
                        content = string.IsNullOrWhiteSpace(finalText) ? "" : finalText,
                        tool_calls = toolCallNodes
                    });
                }
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
                if (string.IsNullOrWhiteSpace(finalText) &&
                    tokenUsage.HasAny)
                {
                    EmitStreamingText(request.ConversationId, string.Empty, isComplete: true,
                        inputTokens: tokenUsage.InputTokens,
                        outputTokens: tokenUsage.OutputTokensOrNull,
                        model: responseModel,
                        reasoningContent: string.IsNullOrEmpty(reasoningText) ? null : reasoningText,
                        cacheReadTokens: tokenUsage.CacheReadTokens);
                }

                responseBuilder.AppendLine("\n\n[Conversation stopped: Tool repeatedly failed]");
                break;
            }

            if (shouldBreakForApproval)
            {
                breakAfterPlanPresentation = true;
                continue;
            }
        }

        return new ProviderResult(responseBuilder.ToString().Trim(), iterationCount);
    }

    private async Task<(string finalText, string reasoningText, string? reasoningFieldName, List<ToolCallAccumulator> toolCalls, int? inputTokens, int? outputTokens, string? responseModel, int? cacheReadTokens)>
        StreamOnceAsync(ChatClient client, List<ChatMessage> messages, ChatCompletionOptions options,
            string conversationId, int streamingTimeoutMinutes, CancellationToken cancellationToken)
    {
        var currentText = new StringBuilder();
        var reasoningText = new StringBuilder();
        var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();
        int? inputTokens = null;
        int? outputTokens = null;
        int? cacheReadTokens = null;
        string? responseModel = null;
        string? reasoningFieldName = null;
        var lastEmitTime = DateTime.UtcNow;
        const int emitIntervalMs = 100;

        // Guard against providers that hang indefinitely (no first chunk, stream never closes).
        using var streamTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        streamTimeoutCts.CancelAfter(TimeSpan.FromMinutes(streamingTimeoutMinutes));

        var streamingUpdate = client.CompleteChatStreamingAsync(messages, options, streamTimeoutCts.Token);
        await foreach (var chunk in streamingUpdate.WithCancellation(streamTimeoutCts.Token))
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

                // Reasoning content — field name varies by provider:
                //   Kimi / Moonshot: delta.reasoning_content (string)
                //   OpenRouter:      delta.reasoning (string)
                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("reasoning_content", out var rc) &&
                        rc.ValueKind == JsonValueKind.String)
                    {
                        var rcText = rc.GetString();
                        if (!string.IsNullOrEmpty(rcText))
                        {
                            reasoningText.Append(rcText);
                            reasoningFieldName = "reasoning_content";
                        }
                    }
                    else if (delta.TryGetProperty("reasoning", out var ro) &&
                             ro.ValueKind == JsonValueKind.String)
                    {
                        var roText = ro.GetString();
                        if (!string.IsNullOrEmpty(roText))
                        {
                            reasoningText.Append(roText);
                            reasoningFieldName = "reasoning";
                        }
                    }
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

        return (currentText.ToString(), reasoningText.ToString(), reasoningFieldName,
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

    private ChatClient BuildChatClient(string model, string apiKey, string? extraBodyJson = null)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri($"{_baseUrl}/")
        };
        if (!string.IsNullOrWhiteSpace(extraBodyJson))
            options.AddPolicy(new JsonBodyMergePolicy(extraBodyJson), System.ClientModel.Primitives.PipelinePosition.PerCall);
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

    private List<ChatMessage> BuildMessages(ConversationRequest request)
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
            if (history.Count > _maxConversationHistoryMessages)
                history = history.Skip(history.Count - _maxConversationHistoryMessages).ToList();

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
        if (string.IsNullOrEmpty(text) &&
            (!isComplete || (!inputTokens.HasValue && !outputTokens.HasValue && !cacheReadTokens.HasValue)))
        {
            return;
        }

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

    /// <summary>
    /// Pipeline policy that merges a JSON object into the request body before it is sent.
    /// Used for provider-specific extra parameters (e.g. {"reasoning":{"enabled":true}} on OpenRouter).
    /// </summary>
    private sealed class JsonBodyMergePolicy : System.ClientModel.Primitives.PipelinePolicy
    {
        private readonly JsonDocument _extra;

        public JsonBodyMergePolicy(string extraBodyJson)
        {
            _extra = JsonDocument.Parse(extraBodyJson);
        }

        public override void Process(
            System.ClientModel.Primitives.PipelineMessage message,
            IReadOnlyList<System.ClientModel.Primitives.PipelinePolicy> pipeline,
            int currentIndex)
        {
            MergeBody(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(
            System.ClientModel.Primitives.PipelineMessage message,
            IReadOnlyList<System.ClientModel.Primitives.PipelinePolicy> pipeline,
            int currentIndex)
        {
            MergeBody(message);
            await ProcessNextAsync(message, pipeline, currentIndex);
        }

        private void MergeBody(System.ClientModel.Primitives.PipelineMessage message)
        {
            if (message.Request.Content is null) return;

            using var ms = new System.IO.MemoryStream();
            message.Request.Content.WriteTo(ms, default);
            ms.Position = 0;

            try
            {
                using var original = JsonDocument.Parse(ms);
                using var merged = new System.IO.MemoryStream();
                using (var writer = new Utf8JsonWriter(merged))
                {
                    writer.WriteStartObject();
                    // Write all original fields
                    foreach (var prop in original.RootElement.EnumerateObject())
                        prop.WriteTo(writer);
                    // Merge extra fields (skipping any that already exist)
                    foreach (var prop in _extra.RootElement.EnumerateObject())
                    {
                        if (!original.RootElement.TryGetProperty(prop.Name, out _))
                            prop.WriteTo(writer);
                    }
                    writer.WriteEndObject();
                }
                message.Request.Content = System.ClientModel.BinaryContent.Create(
                    BinaryData.FromBytes(merged.ToArray()));
            }
            catch
            {
                // If body isn't valid JSON, send it unmodified
            }
        }
    }
}
