using Automaton.Mcp.Models;
using Automaton.Mcp.SystemPrompts;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Mcp.Client.Providers;

/// <summary>
/// Google Gemini provider implementation using the Gemini REST API with SSE streaming
/// </summary>
public class GoogleProvider : IAiProvider
{
    private readonly ILogger _logger;

    /// <summary>
    /// Maximum number of conversation messages to keep in history (sliding window).
    /// Prevents unbounded token growth. Each turn = 2 messages (user + assistant).
    /// 10 messages = last 5 turns, typically 2,000-5,000 tokens saved per call.
    /// </summary>
    private const int MaxConversationHistoryMessages = 10;

    /// <inheritdoc />
    public string ProviderName => "Google";

    /// <inheritdoc />
    public string DefaultModel => "gemini-2.0-flash";

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    /// <summary>
    /// Shared HttpClient with infinite timeout for streaming Gemini calls.
    /// CancellationToken controls actual request lifetime — not the client timeout.
    /// </summary>
    private static readonly HttpClient GeminiHttpClient = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    public GoogleProvider(ILogger logger)
    {
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
            return await CallGeminiAsync(request, tools, executeToolAsync, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Google Gemini conversation cancelled by client");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Google Gemini API");
            throw;
        }
    }

    private async Task<ProviderResult> CallGeminiAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        var toolList = tools.ToList();
        var contents = BuildContents(request);
        var responseBuilder = new StringBuilder();

        var maxIterations = IterationLimits.Resolve(null, request.MaxIterations);
        var maxConsecutiveFailures = request.MaxConsecutiveFailures > 0 ? request.MaxConsecutiveFailures : 3;
        var iterationCount = 0;
        var consecutiveFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Accumulate tokens across all iterations.
        // Gemini reports promptTokenCount cumulatively (keep the last/largest value).
        // candidatesTokenCount is per-call, so we sum across iterations.
        int? totalInputTokens = null;
        int totalOutputTokens = 0;
        string? modelVersion = null;

        while (true)
        {
            iterationCount++;
            if (iterationCount > maxIterations)
            {
                _logger.LogWarning("Conversation exceeded maximum iterations ({MaxIterations}), stopping", maxIterations);
                responseBuilder.AppendLine("\n\n[Conversation stopped: Maximum tool call iterations reached]");
                break;
            }

            // Stream the response, collecting text and function calls
            var currentText = new StringBuilder();
            var functionCalls = new List<GeminiFunctionCall>();
            var rawModelParts = new List<JsonElement>(); // preserve all parts verbatim (incl. thought_signature)
            int? iterInputTokens = null;
            int? iterOutputTokens = null;
            var lastEmitTime = DateTime.UtcNow;
            const int emitIntervalMs = 100;

            // Rate-limit retry pipeline — same strategy as Anthropic/OpenAI providers
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
                            "Gemini rate limit hit (attempt {Retry}/{MaxRetries}). Model={Model}, Retrying in {Delay}s. Reason: {Error}",
                            args.AttemptNumber + 1, request.MaxRateLimitRetries,
                            request.Model, delaySecs, args.Outcome.Exception?.Message);
                        EmitRetryNotification(request.ConversationId, args.AttemptNumber + 1, request.MaxRateLimitRetries, delaySecs);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();

            // Reset per-iteration accumulators before each retry
            currentText.Clear();
            functionCalls.Clear();
            rawModelParts.Clear();
            iterInputTokens = null;
            iterOutputTokens = null;

            await retryPipeline.ExecuteAsync(async ct =>
            {
                currentText.Clear();
                functionCalls.Clear();
                rawModelParts.Clear();
                iterInputTokens = null;
                iterOutputTokens = null;

                await foreach (var chunk in StreamGenerateContentAsync(
                    request.Model ?? DefaultModel,
                    request.ApiKey,
                    request.SystemPromptParts,
                    request.SystemPrompt,
                    request.MaxTokens,
                    contents,
                    toolList,
                    ct))
                {
                    if (chunk.InputTokens.HasValue) iterInputTokens = chunk.InputTokens;
                    if (chunk.OutputTokens.HasValue) iterOutputTokens = chunk.OutputTokens;
                    if (chunk.ModelVersion != null) modelVersion = chunk.ModelVersion;

                    if (!string.IsNullOrEmpty(chunk.TextDelta))
                    {
                        currentText.Append(chunk.TextDelta);

                        var now = DateTime.UtcNow;
                        if ((now - lastEmitTime).TotalMilliseconds >= emitIntervalMs)
                        {
                            EmitStreamingTextUpdate(request.ConversationId, currentText.ToString(), isComplete: false);
                            lastEmitTime = now;
                        }
                    }

                    functionCalls.AddRange(chunk.FunctionCalls);
                    rawModelParts.AddRange(chunk.RawParts);
                }
            }, cancellationToken);

            // Accumulate tokens across iterations.
            // inputTokens is cumulative in Gemini (includes all prior turns) — keep the latest value.
            // outputTokens is per-call — sum them.
            if (iterInputTokens.HasValue) totalInputTokens = iterInputTokens;
            if (iterOutputTokens.HasValue) totalOutputTokens += iterOutputTokens.Value;

            var textContent = currentText.ToString();

            if (functionCalls.Count == 0)
            {
                // No tool calls — emit final text and finish
                if (!string.IsNullOrWhiteSpace(textContent))
                {
                    EmitStreamingTextUpdate(request.ConversationId, textContent, isComplete: true,
                        inputTokens: totalInputTokens, outputTokens: totalOutputTokens, model: modelVersion ?? request.Model);
                    responseBuilder.AppendLine(textContent);
                }
                break;
            }

            // Emit text portion before tool calls (if any).
            // Pass accumulated tokens so usage is recorded even for intermediate turns.
            if (!string.IsNullOrWhiteSpace(textContent))
            {
                EmitStreamingTextUpdate(request.ConversationId, textContent, isComplete: true,
                    inputTokens: totalInputTokens, outputTokens: totalOutputTokens, model: modelVersion ?? request.Model);
            }
            else if (totalInputTokens.HasValue || totalOutputTokens > 0)
            {
                // Pure tool-call turn (no text) — emit a token-only update so usage is still recorded.
                EmitStreamingTextUpdate(request.ConversationId, string.Empty, isComplete: true,
                    inputTokens: totalInputTokens, outputTokens: totalOutputTokens, model: modelVersion ?? request.Model);
            }

            // Add model response back to contents using raw parts verbatim.
            // This preserves thought_signature fields required by Gemini thinking models.
            contents.Add(new Dictionary<string, object>
            {
                ["role"] = "model",
                ["parts"] = rawModelParts.Count > 0 ? (object)rawModelParts : new List<object>()
            });

            // Execute all tool calls concurrently — results are order-independent
            var functionResponseParts = new List<object>();
            var shouldStop              = false;
            var shouldBreakForApproval  = false;

            var toolTasks = functionCalls.Select(async funcCall =>
            {
                var callId   = $"call_{Guid.NewGuid():N}";
                var argsJson = funcCall.Args.ValueKind != JsonValueKind.Undefined
                    ? JsonSerializer.Serialize(funcCall.Args)
                    : "{}";

                EmitToolCallStarted(request.ConversationId, callId, funcCall.Name, argsJson);
                var toolResult = await executeToolAsync(callId, funcCall.Name, argsJson);
                EmitToolCallCompleted(request.ConversationId, callId, funcCall.Name, argsJson, toolResult);

                return (funcCall, callId, argsJson, toolResult);
            });

            var toolResults = await Task.WhenAll(toolTasks);

            foreach (var (funcCall, callId, argsJson, toolResult) in toolResults)
            {
                var isFailure = toolResult.StartsWith("Tool execution failed:") ||
                                toolResult.StartsWith("Error executing tool") ||
                                toolResult.Contains("A second operation was started");

                if (isFailure)
                {
                    consecutiveFailures.TryGetValue(funcCall.Name, out var current);
                    consecutiveFailures[funcCall.Name] = current + 1;

                    if (consecutiveFailures[funcCall.Name] >= maxConsecutiveFailures)
                    {
                        _logger.LogWarning(
                            "Tool {ToolName} failed {FailCount} consecutive times, stopping",
                            funcCall.Name, consecutiveFailures[funcCall.Name]);
                        shouldStop = true;
                    }
                }
                else
                {
                    consecutiveFailures[funcCall.Name] = 0;
                }

                if (ProviderHelpers.ToolResultAwaitsApproval(funcCall.Name, toolResult))
                    shouldBreakForApproval = true;

                functionResponseParts.Add(new Dictionary<string, object>
                {
                    ["functionResponse"] = new Dictionary<string, object>
                    {
                        ["name"]     = funcCall.Name,
                        ["response"] = new Dictionary<string, object> { ["result"] = toolResult }
                    }
                });
            }

            // Add tool results as a user turn
            contents.Add(new Dictionary<string, object>
            {
                ["role"] = "user",
                ["parts"] = functionResponseParts
            });

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

    /// <summary>
    /// Streams the Gemini response via SSE (streamGenerateContent?alt=sse).
    /// Yields parsed chunks containing text deltas, function calls, and usage metadata.
    /// </summary>
    private async IAsyncEnumerable<GeminiStreamChunk> StreamGenerateContentAsync(
        string model,
        string apiKey,
        SystemPromptParts? promptParts,
        string? systemPromptFallback,
        int maxTokens,
        List<Dictionary<string, object>> contents,
        List<ProviderTool> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = BuildRequestBody(promptParts, systemPromptFallback, maxTokens, contents, tools);
        var json = JsonSerializer.Serialize(requestBody);

        using var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
        var url = new Uri($"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:streamGenerateContent?key={Uri.EscapeDataString(apiKey)}&alt=sse");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = requestContent
        };

        using var response = await GeminiHttpClient.SendAsync(
            requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Gemini streaming API error: {Status} {Body}", response.StatusCode, errorBody);

            string errorDetail = errorBody;
            try
            {
                using var errDoc = JsonDocument.Parse(errorBody);
                if (errDoc.RootElement.TryGetProperty("error", out var errEl) &&
                    errEl.TryGetProperty("message", out var msgEl))
                {
                    errorDetail = msgEl.GetString() ?? errorBody;
                }
            }
            catch { /* fall back to raw body */ }

            throw new InvalidOperationException($"Gemini API error: {response.StatusCode} - {errorDetail}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;

            // SSE format: lines starting with "data: " contain JSON payloads
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var jsonData = line.Substring(6).Trim();
            if (string.IsNullOrEmpty(jsonData) || jsonData == "[DONE]") break;

            GeminiStreamChunk? chunk = null;
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                chunk = ParseChunk(doc.RootElement);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Gemini SSE chunk: {Data}", jsonData);
                continue;
            }

            if (chunk != null)
                yield return chunk;
        }
    }

    private GeminiStreamChunk ParseChunk(JsonElement root)
    {
        var chunk = new GeminiStreamChunk();

        // Extract usage metadata (present in final chunks)
        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            if (usage.TryGetProperty("promptTokenCount", out var inp))
                chunk.InputTokens = inp.GetInt32();

            // candidatesTokenCount = text output tokens
            // thoughtsTokenCount = reasoning tokens (thinking models)
            // Sum both to get the full output token count charged by the API
            var candidateTokens = 0;
            var thoughtTokens = 0;
            if (usage.TryGetProperty("candidatesTokenCount", out var outp))
                candidateTokens = outp.GetInt32();
            if (usage.TryGetProperty("thoughtsTokenCount", out var thoughts_))
                thoughtTokens = thoughts_.GetInt32();
            if (candidateTokens > 0 || thoughtTokens > 0)
                chunk.OutputTokens = candidateTokens + thoughtTokens;
        }

        if (root.TryGetProperty("modelVersion", out var mv))
            chunk.ModelVersion = mv.GetString();

        // Extract content parts from the first candidate
        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            return chunk;
        }

        var candidate = candidates[0];

        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            return chunk;
        }

        foreach (var part in parts.EnumerateArray())
        {
            // Preserve every part verbatim (thought, functionCall, text) so thought_signature
            // is included when we echo the model turn back for thinking models.
            chunk.RawParts.Add(part.Clone());

            if (part.TryGetProperty("text", out var textEl))
            {
                // Only emit visible text (skip thought parts which have "thought: true")
                var isThought = part.TryGetProperty("thought", out var thoughtEl) && thoughtEl.ValueKind == JsonValueKind.True;
                if (!isThought)
                    chunk.TextDelta = textEl.GetString() ?? string.Empty;
            }
            else if (part.TryGetProperty("functionCall", out var funcCallEl))
            {
                chunk.FunctionCalls.Add(new GeminiFunctionCall
                {
                    Name = funcCallEl.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Args = funcCallEl.TryGetProperty("args", out var a) ? a.Clone() : default
                });
            }
        }

        return chunk;
    }

    private List<Dictionary<string, object>> BuildContents(ConversationRequest request)
    {
        var contents = new List<Dictionary<string, object>>();

        if (request.ConversationHistory?.Any() == true)
        {
            var history = request.ConversationHistory;

            if (history.Count > MaxConversationHistoryMessages)
            {
                var skip = history.Count - MaxConversationHistoryMessages;
                history = history.Skip(skip).ToList();

                _logger.LogInformation(
                    "Conversation history truncated: {Total} -> {Kept} messages (sliding window)",
                    request.ConversationHistory.Count, history.Count);
            }

            foreach (var msg in history)
            {
                // Gemini uses "user" and "model" roles (not "assistant")
                var role = msg.Role.ToLowerInvariant() == "assistant" ? "model" : "user";
                contents.Add(new Dictionary<string, object>
                {
                    ["role"] = role,
                    ["parts"] = new List<object> { new Dictionary<string, object> { ["text"] = msg.Content } }
                });
            }
        }

        contents.Add(new Dictionary<string, object>
        {
            ["role"] = "user",
            ["parts"] = new List<object> { new Dictionary<string, object> { ["text"] = request.InitialMessage } }
        });

        return contents;
    }

    private static Dictionary<string, object?> BuildRequestBody(
        SystemPromptParts? promptParts,
        string? systemPromptFallback,
        int maxTokens,
        List<Dictionary<string, object>> contents,
        List<ProviderTool> tools)
    {
        // Gemini's system_instruction is a single block — put only the stable BasePrompt here.
        // InstructionBlock + MemoryBlock are prepended as the first user turn so they sit
        // after the cached system_instruction prefix (implicit caching on Gemini 2.5+).
        string? basePrompt   = promptParts?.BasePrompt ?? systemPromptFallback;
        string? extraContext = BuildExtraContext(promptParts);

        var effectiveContents = contents;
        if (!string.IsNullOrWhiteSpace(extraContext))
        {
            // Inject a preamble user turn with per-app/session context before conversation history
            effectiveContents = new List<Dictionary<string, object>>
            {
                new() { ["role"] = "user",  ["parts"] = new List<object> { new Dictionary<string, object> { ["text"] = extraContext } } },
                new() { ["role"] = "model", ["parts"] = new List<object> { new Dictionary<string, object> { ["text"] = "Understood." } } }
            };
            effectiveContents.AddRange(contents);
        }

        var requestBody = new Dictionary<string, object?>
        {
            ["contents"] = effectiveContents
        };

        if (!string.IsNullOrWhiteSpace(basePrompt))
        {
            requestBody["system_instruction"] = new Dictionary<string, object>
            {
                ["parts"] = new List<object> { new Dictionary<string, object> { ["text"] = basePrompt } }
            };
        }

        if (tools.Count > 0)
        {
            var functionDeclarations = tools.Select(tool => new Dictionary<string, object?>
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = BuildParameterSchema(tool.InputSchemaJson)
            }).ToList();

            requestBody["tools"] = new List<object>
            {
                new Dictionary<string, object> { ["function_declarations"] = functionDeclarations }
            };
            requestBody["tool_config"] = new Dictionary<string, object>
            {
                ["function_calling_config"] = new Dictionary<string, object> { ["mode"] = "AUTO" }
            };

            // Disable thinking when tools are involved. Thinking models (gemini-2.5+) embed a
            // thought_signature in function calls that must be echoed verbatim. Since streaming
            // can split parts across chunks making faithful reconstruction unreliable, we simply
            // turn thinking off for tool-calling turns — matching OpenAI/Anthropic behaviour.
            requestBody["generationConfig"] = new Dictionary<string, object?>
            {
                ["thinkingConfig"] = new Dictionary<string, object> { ["thinkingBudget"] = 0 },
                ["maxOutputTokens"] = maxTokens > 0 ? (object?)maxTokens : null
            };
        }
        else if (maxTokens > 0)
        {
            requestBody["generationConfig"] = new Dictionary<string, object?> { ["maxOutputTokens"] = maxTokens };
        }

        return requestBody;
    }

    private static string? BuildExtraContext(SystemPromptParts? parts)
    {
        if (parts == null) return null;
        var segments = new[] { parts.InstructionBlock, parts.MemoryBlock }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var joined = string.Join("\n\n", segments);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static bool IsRateLimitException(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("insufficient_quota",       StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("quota_exceeded",           StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("exceeded_current_quota",   StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("insufficient balance",     StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("suspended",                StringComparison.OrdinalIgnoreCase))
            return false;

        return msg.Contains("RESOURCE_EXHAUSTED",  StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("rate_limit",           StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("429",                  StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Too Many Requests",    StringComparison.OrdinalIgnoreCase);
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

    private static object BuildParameterSchema(string inputSchemaJson)
    {
        if (string.IsNullOrEmpty(inputSchemaJson) || inputSchemaJson == "{}")
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>()
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(inputSchemaJson);
            return doc.RootElement.Clone();
        }
        catch
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>()
            };
        }
    }

    private void EmitStreamingTextUpdate(string conversationId, string text, bool isComplete,
        int? inputTokens = null, int? outputTokens = null, string? model = null)
    {
        if (string.IsNullOrEmpty(conversationId) || string.IsNullOrEmpty(text))
            return;

        var message = new ConversationMessage("assistant", text)
        {
            IsStreaming = !isComplete,
            InputTokens = isComplete ? inputTokens : null,
            OutputTokens = isComplete ? outputTokens : null,
            Model = isComplete ? model : null
        };

        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = new List<ConversationMessage> { message },
            ToolCalls = new List<ToolCall>(),
            ToolResults = new List<ToolResult>()
        });
    }

    private void EmitToolCallStarted(string conversationId, string toolCallId, string toolName, string argsJson)
    {
        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = new List<ConversationMessage>(),
            ToolCalls = new List<ToolCall>
            {
                new ToolCall { Id = toolCallId, Name = toolName, Arguments = ParseToolArguments(argsJson) }
            },
            ToolResults = new List<ToolResult>()
        });
    }

    private void EmitToolCallCompleted(string conversationId, string toolCallId, string toolName, string argsJson, string result)
    {
        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = new List<ConversationMessage>(),
            ToolCalls = new List<ToolCall>
            {
                new ToolCall { Id = toolCallId, Name = toolName, Arguments = ParseToolArguments(argsJson) }
            },
            ToolResults = new List<ToolResult>
            {
                new ToolResult
                {
                    ToolCallId = toolCallId,
                    Result = result,
                    IsSuccess = !result.StartsWith("Tool execution failed:") && !result.StartsWith("Error executing tool"),
                    ErrorMessage = result.StartsWith("Tool execution failed:") || result.StartsWith("Error executing tool") ? result : null
                }
            }
        });
    }

    private static Dictionary<string, object?> ParseToolArguments(string argsJson)
    {
        try
        {
            if (string.IsNullOrEmpty(argsJson) || argsJson == "{}")
                return new Dictionary<string, object?>();

            return JsonDocument.Parse(argsJson).RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => (object?)p.Value.ToString());
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private class GeminiStreamChunk
    {
        public string? TextDelta { get; set; }
        public List<GeminiFunctionCall> FunctionCalls { get; set; } = new();
        public List<JsonElement> RawParts { get; set; } = new();
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public string? ModelVersion { get; set; }
    }

    private class GeminiFunctionCall
    {
        public string Name { get; set; } = string.Empty;
        public JsonElement Args { get; set; }
    }
}
