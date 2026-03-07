using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

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

    public async Task<string> CallAsync(
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

            ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
            {
                ConversationId = request.ConversationId,
                Messages = new List<ConversationMessage>
                {
                    new ConversationMessage("assistant", $"Error: {ex.Message}")
                },
                ToolCalls = new List<ToolCall>(),
                ToolResults = new List<ToolResult>()
            });

            throw;
        }
    }

    private async Task<string> CallGeminiAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        var toolList = tools.ToList();
        var contents = BuildContents(request);
        var responseBuilder = new StringBuilder();

        var maxIterations = request.MaxIterations > 0 ? request.MaxIterations : 20;
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

            await foreach (var chunk in StreamGenerateContentAsync(
                request.Model ?? "gemini-2.0-flash",
                request.ApiKey,
                request.SystemPrompt,
                contents,
                toolList,
                cancellationToken))
            {
                if (chunk.InputTokens.HasValue) iterInputTokens = chunk.InputTokens;
                if (chunk.OutputTokens.HasValue) iterOutputTokens = chunk.OutputTokens;
                if (chunk.ModelVersion != null) modelVersion = chunk.ModelVersion;

                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    currentText.Append(chunk.TextDelta);

                    // Throttle streaming updates (same pattern as Anthropic)
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

            // Execute each function call and collect results
            var functionResponseParts = new List<object>();
            var shouldStop = false;
            var planGate = new PlanGateTracker();

            foreach (var funcCall in functionCalls)
            {
                var callId = $"call_{Guid.NewGuid():N}";
                var argsJson = funcCall.Args.ValueKind != JsonValueKind.Undefined
                    ? JsonSerializer.Serialize(funcCall.Args)
                    : "{}";

                EmitToolCallStarted(request.ConversationId, callId, funcCall.Name, argsJson);

                var toolResult = await executeToolAsync(callId, funcCall.Name, argsJson);

                planGate.CheckToolResult(funcCall.Name, toolResult);

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

                        toolResult += $"\n\n[CRITICAL: This tool has failed {consecutiveFailures[funcCall.Name]} times consecutively. " +
                                      "Please stop calling this tool and inform the user about the issue.]";
                        shouldStop = true;
                    }
                }
                else
                {
                    consecutiveFailures[funcCall.Name] = 0;
                }

                EmitToolCallCompleted(request.ConversationId, callId, funcCall.Name, argsJson, toolResult);

                functionResponseParts.Add(new Dictionary<string, object>
                {
                    ["functionResponse"] = new Dictionary<string, object>
                    {
                        ["name"] = funcCall.Name,
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

            if (planGate.ShouldBreakLoop(request.Mode))
                break;
        }

        return responseBuilder.ToString().Trim();
    }

    /// <summary>
    /// Streams the Gemini response via SSE (streamGenerateContent?alt=sse).
    /// Yields parsed chunks containing text deltas, function calls, and usage metadata.
    /// </summary>
    private async IAsyncEnumerable<GeminiStreamChunk> StreamGenerateContentAsync(
        string model,
        string apiKey,
        string? systemPrompt,
        List<Dictionary<string, object>> contents,
        List<ProviderTool> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = BuildRequestBody(systemPrompt, contents, tools);
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

            _logger.LogDebug("Gemini usage: prompt={Input} candidates={Candidates} thoughts={Thoughts} total={Total}",
                chunk.InputTokens, candidateTokens, thoughtTokens,
                usage.TryGetProperty("totalTokenCount", out var tot) ? tot.GetInt32() : (int?)null);
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
        string? systemPrompt,
        List<Dictionary<string, object>> contents,
        List<ProviderTool> tools)
    {
        var requestBody = new Dictionary<string, object?>
        {
            ["contents"] = contents
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            requestBody["system_instruction"] = new Dictionary<string, object>
            {
                ["parts"] = new List<object> { new Dictionary<string, object> { ["text"] = systemPrompt } }
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
            requestBody["generationConfig"] = new Dictionary<string, object>
            {
                ["thinkingConfig"] = new Dictionary<string, object>
                {
                    ["thinkingBudget"] = 0
                }
            };
        }

        return requestBody;
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
