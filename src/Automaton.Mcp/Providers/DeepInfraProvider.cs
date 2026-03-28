using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Automaton.Mcp.Models;
using Automaton.Mcp.SystemPrompts;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Automaton.Mcp.Client.Providers;

/// <summary>
/// Provider for DeepInfra-hosted models (DeepSeek, Qwen, Kimi, etc.) using raw HTTP.
/// Unlike <see cref="OpenAiCompatibleProvider"/>, this provider directly controls the full
/// HTTP request/response cycle, enabling:
/// <list type="bullet">
///   <item>Single merged system prompt — better instruction adherence on third-party hosts</item>
///   <item>Direct temperature and top_p control via <see cref="ConversationRequest.Temperature"/> / <see cref="ConversationRequest.TopP"/></item>
///   <item>Larger conversation history window (30 messages vs 10)</item>
///   <item>Native reasoning_content extraction without OpenAI SDK round-trip</item>
/// </list>
/// <para>
/// Configure a provider in Admin → AI Providers with Name = "DeepInfra" and
/// BaseUrl = "https://api.deepinfra.com/v1/openai" to use this implementation.
/// </para>
/// </summary>
public sealed class DeepInfraProvider : IAiProvider
{
    private readonly ILogger _logger;
    private readonly string _baseUrl;
    private readonly int _maxConversationHistoryMessages;

    /// <summary>
    /// Canonical provider name — set this as the Name in the AiProviders admin table
    /// to route to this implementation instead of <see cref="OpenAiCompatibleProvider"/>.
    /// </summary>
    public const string ProviderKey = "DeepInfra";

    /// <summary>
    // Singleton HttpClient — connection pooling, avoids socket exhaustion
    private static readonly HttpClient _http = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    private static readonly JsonSerializerOptions _serializeOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public string ProviderName { get; }
    public string DefaultModel { get; }
    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    public DeepInfraProvider(
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

    // ─────────────────────────────────────────────────────────
    // Public entry point
    // ─────────────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────
    // Main conversation loop
    // ─────────────────────────────────────────────────────────

    private async Task<ProviderResult> CallChatCompletionsAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrEmpty(request.Model) ? DefaultModel : request.Model;
        var toolList = tools.ToList();
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
                            "{Provider} rate limit hit (attempt {Retry}/{MaxRetries}). Model={Model}, Retrying in {Delay}s",
                            ProviderName, args.AttemptNumber + 1, request.MaxRateLimitRetries, model, delaySecs);
                        EmitRetryNotification(request.ConversationId, args.AttemptNumber + 1, request.MaxRateLimitRetries, delaySecs);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();

            StreamResult streamResult = new(string.Empty, string.Empty, [], null, null, null, null);
            await retryPipeline.ExecuteAsync(async ct =>
            {
                streamResult = await StreamOnceAsync(request, model, messages, toolList, ct);
            }, cancellationToken);

            if (streamResult.InputTokens.HasValue) totalInputTokens = streamResult.InputTokens;
            if (streamResult.OutputTokens.HasValue) totalOutputTokens += streamResult.OutputTokens.Value;
            if (streamResult.CacheReadTokens.HasValue) totalCacheReadTokens = streamResult.CacheReadTokens;

            var finalText = streamResult.FinalText;
            var reasoningText = streamResult.ReasoningText;
            var toolCalls = streamResult.ToolCalls;
            var responseModel = streamResult.ResponseModel ?? model;

            if (toolCalls.Count == 0)
            {
                // No tool calls — this is the final assistant response
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

            // Emit partial assistant text before executing tools
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                EmitStreamingText(request.ConversationId, finalText, isComplete: false,
                    inputTokens: totalInputTokens,
                    outputTokens: totalOutputTokens > 0 ? totalOutputTokens : null,
                    model: responseModel,
                    reasoningContent: string.IsNullOrEmpty(reasoningText) ? null : reasoningText,
                    cacheReadTokens: totalCacheReadTokens);
            }

            // Add assistant message (with tool_calls + optional reasoning_content) back into history
            var assistantMsg = new Dictionary<string, object?>
            {
                ["role"] = "assistant",
                ["content"] = string.IsNullOrWhiteSpace(finalText) ? null : finalText,
                ["tool_calls"] = toolCalls.Select(tc => (object)new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.FunctionName, arguments = tc.ArgumentsBuilder.ToString() }
                }).ToList()
            };
            // Thinking models (kimi-k2.5, DeepSeek-R1) require reasoning_content echoed back
            if (!string.IsNullOrEmpty(reasoningText))
                assistantMsg["reasoning_content"] = reasoningText;
            messages.Add(assistantMsg);

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
                        _logger.LogWarning("Tool {Tool} failed {Count} consecutive times, stopping",
                            tc.FunctionName, consecutiveFailures[tc.FunctionName]);
                        shouldStop = true;
                    }
                }
                else
                {
                    consecutiveFailures[tc.FunctionName] = 0;
                }

                if (ProviderHelpers.ToolResultAwaitsApproval(tc.FunctionName, toolResult))
                    shouldBreakForApproval = true;

                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = tc.Id,
                    ["content"] = toolResult
                });
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

    // ─────────────────────────────────────────────────────────
    // Raw HTTP streaming
    // ─────────────────────────────────────────────────────────

    private async Task<StreamResult> StreamOnceAsync(
        ConversationRequest request,
        string model,
        List<object> messages,
        List<ProviderTool> tools,
        CancellationToken cancellationToken)
    {
        var payload = BuildRequestPayload(model, messages, tools, request);
        var json = JsonSerializer.Serialize(payload, _serializeOptions);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(request.StreamingTimeoutMinutes));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey ?? string.Empty);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            throw new HttpRequestException(
                $"{ProviderName} API returned {(int)response.StatusCode}: {errorBody}",
                null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
        using var reader = new StreamReader(stream);

        var currentText = new StringBuilder();
        var reasoningText = new StringBuilder();
        var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();
        int? inputTokens = null;
        int? outputTokens = null;
        int? cacheReadTokens = null;
        string? responseModel = null;
        var lastEmitTime = DateTime.UtcNow;
        const int emitIntervalMs = 100;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(timeoutCts.Token);
            if (line is null) break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line.Substring(6).Trim();
            if (data == "[DONE]") break;
            if (string.IsNullOrEmpty(data)) continue;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                // Model name (usually in first chunk)
                if (root.TryGetProperty("model", out var modelProp) && modelProp.ValueKind == JsonValueKind.String)
                    responseModel = modelProp.GetString();

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("delta", out var delta) &&
                        delta.ValueKind == JsonValueKind.Object)
                    {
                        // Regular text content
                        if (delta.TryGetProperty("content", out var contentProp) &&
                            contentProp.ValueKind == JsonValueKind.String)
                        {
                            var text = contentProp.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                currentText.Append(text);
                                var now = DateTime.UtcNow;
                                if ((now - lastEmitTime).TotalMilliseconds >= emitIntervalMs)
                                {
                                    EmitStreamingText(request.ConversationId, currentText.ToString(), isComplete: false);
                                    lastEmitTime = now;
                                }
                            }
                        }

                        // reasoning_content — thinking models (kimi-k2.5, DeepSeek-R1)
                        if (delta.TryGetProperty("reasoning_content", out var rc) &&
                            rc.ValueKind == JsonValueKind.String)
                        {
                            var rcText = rc.GetString();
                            if (!string.IsNullOrEmpty(rcText))
                                reasoningText.Append(rcText);
                        }

                        // Tool call deltas (streamed incrementally across chunks)
                        if (delta.TryGetProperty("tool_calls", out var tcArray) &&
                            tcArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var tc in tcArray.EnumerateArray())
                            {
                                var idx = tc.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : 0;
                                if (!toolCallAccumulators.TryGetValue(idx, out var acc))
                                {
                                    acc = new ToolCallAccumulator { Index = idx };
                                    toolCallAccumulators[idx] = acc;
                                }
                                if (tc.TryGetProperty("id", out var tcId) && tcId.ValueKind == JsonValueKind.String)
                                    acc.Id = tcId.GetString()!;
                                if (tc.TryGetProperty("function", out var fn))
                                {
                                    if (fn.TryGetProperty("name", out var fnName) && fnName.ValueKind == JsonValueKind.String)
                                        acc.FunctionName = fnName.GetString()!;
                                    if (fn.TryGetProperty("arguments", out var fnArgs) && fnArgs.ValueKind == JsonValueKind.String)
                                        acc.ArgumentsBuilder.Append(fnArgs.GetString());
                                }
                            }
                        }
                    }
                }

                // Usage stats (present in final chunk when stream_options.include_usage = true)
                if (root.TryGetProperty("usage", out var usage) &&
                    usage.ValueKind == JsonValueKind.Object)
                {
                    if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number)
                        inputTokens = pt.GetInt32();
                    if (usage.TryGetProperty("completion_tokens", out var ct) && ct.ValueKind == JsonValueKind.Number)
                        outputTokens = ct.GetInt32();

                    // Cache hit tokens — DeepInfra / Moonshot style
                    if (usage.TryGetProperty("prompt_cache_hit_tokens", out var cht) && cht.ValueKind == JsonValueKind.Number)
                        cacheReadTokens = cht.GetInt32();
                    else if (usage.TryGetProperty("prompt_tokens_details", out var details) &&
                             details.ValueKind == JsonValueKind.Object &&
                             details.TryGetProperty("cached_tokens", out var cached) &&
                             cached.ValueKind == JsonValueKind.Number)
                        cacheReadTokens = cached.GetInt32();
                }
            }
            catch (JsonException)
            {
                // Malformed SSE chunk — skip and continue
            }
        }

        return new StreamResult(
            currentText.ToString(),
            reasoningText.ToString(),
            toolCallAccumulators.Values.OrderBy(t => t.Index).ToList(),
            inputTokens, outputTokens, responseModel, cacheReadTokens);
    }

    // ─────────────────────────────────────────────────────────
    // Request building
    // ─────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildRequestPayload(
        string model,
        List<object> messages,
        List<ProviderTool> tools,
        ConversationRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true,
            // Ask the provider to include usage in the final [DONE] chunk
            ["stream_options"] = new Dictionary<string, object> { ["include_usage"] = true }
        };

        if (request.MaxTokens > 0)  payload["max_tokens"]  = request.MaxTokens;
        if (request.Temperature.HasValue) payload["temperature"] = request.Temperature.Value;
        if (request.TopP.HasValue)  payload["top_p"]  = request.TopP.Value;

        if (tools.Count > 0)
        {
            payload["tools"] = tools.Select(t => (object)new Dictionary<string, object?>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = ParseSchemaElement(t.InputSchemaJson)
                }
            }).ToList();
            payload["tool_choice"] = "auto";
        }

        return payload;
    }

    private static object ParseSchemaElement(string inputSchemaJson)
    {
        if (string.IsNullOrEmpty(inputSchemaJson) || inputSchemaJson == "{}")
            return new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object>() };
        try
        {
            // Return JsonElement — serializes verbatim, no re-parsing
            return JsonDocument.Parse(inputSchemaJson).RootElement;
        }
        catch
        {
            return new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object>() };
        }
    }

    /// <summary>
    /// Builds messages list with a single merged system prompt.
    /// DeepInfra-hosted models respond more reliably to one coherent system message
    /// than to multiple system messages (the OpenAI automatic-caching optimisation).
    /// </summary>
    private List<object> BuildMessages(ConversationRequest request)
    {
        var messages = new List<object>();

        var systemContent = request.SystemPromptParts?.Full ?? request.SystemPrompt;
        if (!string.IsNullOrEmpty(systemContent))
            messages.Add(new Dictionary<string, object?> { ["role"] = "system", ["content"] = systemContent });

        if (request.ConversationHistory?.Count > 0)
        {
            var history = request.ConversationHistory;
            if (history.Count > _maxConversationHistoryMessages)
                history = history.Skip(history.Count - _maxConversationHistoryMessages).ToList();

            foreach (var msg in history)
            {
                var role = msg.Role.ToLowerInvariant() switch
                {
                    "assistant" => "assistant",
                    "system"    => "system",
                    _           => "user"
                };
                messages.Add(new Dictionary<string, object?> { ["role"] = role, ["content"] = msg.Content });
            }
        }

        messages.Add(new Dictionary<string, object?> { ["role"] = "user", ["content"] = request.InitialMessage });
        return messages;
    }

    // ─────────────────────────────────────────────────────────
    // Event emission helpers
    // ─────────────────────────────────────────────────────────

    private void EmitStreamingText(string conversationId, string text, bool isComplete,
        int? inputTokens = null, int? outputTokens = null, string? model = null,
        string? reasoningContent = null, int? cacheReadTokens = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        var message = new ConversationMessage("assistant", text)
        {
            IsStreaming      = !isComplete,
            InputTokens      = isComplete ? inputTokens : null,
            OutputTokens     = isComplete ? outputTokens : null,
            Model            = isComplete ? model : null,
            ReasoningContent = isComplete ? reasoningContent : null,
            CacheReadTokens  = isComplete ? cacheReadTokens : null
        };
        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages       = [message],
            ToolCalls      = [],
            ToolResults    = []
        });
    }

    private void EmitToolCallStarted(string conversationId, string toolCallId, string toolName, string argsJson)
    {
        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages       = [],
            ToolCalls      = [new ToolCall { Id = toolCallId, Name = toolName, Arguments = ParseArgs(argsJson) }],
            ToolResults    = []
        });
    }

    private void EmitToolCallCompleted(string conversationId, string toolCallId, string toolName, string argsJson, string result)
    {
        var isSuccess = !result.StartsWith("Tool execution failed:") && !result.StartsWith("Error executing tool");
        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages       = [],
            ToolCalls      = [new ToolCall { Id = toolCallId, Name = toolName, Arguments = ParseArgs(argsJson) }],
            ToolResults    = [new ToolResult { ToolCallId = toolCallId, Result = result, IsSuccess = isSuccess, ErrorMessage = isSuccess ? null : result }]
        });
    }

    private void EmitRetryNotification(string conversationId, int attempt, int maxRetries, int delaySeconds)
    {
        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages       = [new ConversationMessage("assistant",
                $"⏳ Rate limit reached. Retrying in {delaySeconds}s (attempt {attempt}/{maxRetries})...")],
            ToolCalls      = [],
            ToolResults    = []
        });
    }

    // ─────────────────────────────────────────────────────────
    // Retry helpers
    // ─────────────────────────────────────────────────────────

    private static bool IsRateLimitException(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("insufficient_quota",    StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("quota_exceeded",        StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("insufficient balance",  StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("suspended",             StringComparison.OrdinalIgnoreCase))
            return false;

        return ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } ||
               msg.Contains("429",              StringComparison.Ordinal) ||
               msg.Contains("rate_limit",       StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex RetryAfterPattern = new(
        @"retry[- ](?:after|in)\D*(?<secs>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    // ─────────────────────────────────────────────────────────
    // Static helpers
    // ─────────────────────────────────────────────────────────

    private static Dictionary<string, object?> ParseArgs(string argsJson)
    {
        try
        {
            if (string.IsNullOrEmpty(argsJson) || argsJson == "{}") return [];
            return JsonDocument.Parse(argsJson).RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => (object?)p.Value.ToString());
        }
        catch
        {
            return [];
        }
    }

    // ─────────────────────────────────────────────────────────
    // Private types
    // ─────────────────────────────────────────────────────────

    private sealed record StreamResult(
        string FinalText,
        string ReasoningText,
        List<ToolCallAccumulator> ToolCalls,
        int? InputTokens,
        int? OutputTokens,
        string? ResponseModel,
        int? CacheReadTokens);

    private sealed class ToolCallAccumulator
    {
        public int Index { get; set; }
        public string Id { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
        public StringBuilder ArgumentsBuilder { get; } = new();
    }
}
