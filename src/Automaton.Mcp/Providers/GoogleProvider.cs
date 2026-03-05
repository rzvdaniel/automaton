using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Automaton.Mcp.Client.Providers;

/// <summary>
/// Google Gemini provider implementation using the Gemini REST API
/// </summary>
public class GoogleProvider : IAiProvider
{
    private readonly ILogger _logger;

    private const int MaxConversationHistoryMessages = 10;

    /// <inheritdoc />
    public string ProviderName => "Google";

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    private static readonly HttpClient GeminiHttpClient = new()
    {
        BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/models/"),
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

        while (true)
        {
            iterationCount++;
            if (iterationCount > maxIterations)
            {
                _logger.LogWarning("Conversation exceeded maximum iterations ({MaxIterations}), stopping", maxIterations);
                responseBuilder.AppendLine("\n\n[Conversation stopped: Maximum tool call iterations reached]");
                break;
            }

            var response = await SendGenerateContentAsync(
                request.Model ?? "gemini-2.0-flash",
                request.ApiKey,
                request.SystemPrompt,
                contents,
                toolList,
                cancellationToken);

            // Extract text and function calls from response parts
            var textParts = new List<string>();
            var functionCalls = new List<GeminiFunctionCall>();

            if (response.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array &&
                candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textEl))
                        {
                            textParts.Add(textEl.GetString() ?? string.Empty);
                        }
                        else if (part.TryGetProperty("functionCall", out var funcCallEl))
                        {
                            functionCalls.Add(new GeminiFunctionCall
                            {
                                Name = funcCallEl.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                                Args = funcCallEl.TryGetProperty("args", out var a) ? a.Clone() : default
                            });
                        }
                    }
                }
            }

            // Extract token usage
            int? inputTokens = null;
            int? outputTokens = null;
            string? modelVersion = null;

            if (response.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var inp))
                    inputTokens = inp.GetInt32();
                if (usage.TryGetProperty("candidatesTokenCount", out var out_))
                    outputTokens = out_.GetInt32();
            }
            if (response.TryGetProperty("modelVersion", out var mv))
                modelVersion = mv.GetString();

            var textContent = string.Join("\n", textParts.Where(t => !string.IsNullOrWhiteSpace(t)));

            if (functionCalls.Count == 0)
            {
                // No tool calls — emit text and finish
                if (!string.IsNullOrWhiteSpace(textContent))
                {
                    EmitStreamingTextUpdate(request.ConversationId, textContent, isComplete: true,
                        inputTokens: inputTokens, outputTokens: outputTokens, model: modelVersion ?? request.Model);
                    responseBuilder.AppendLine(textContent);
                }
                break;
            }

            // Emit text portion (if any) before tool calls
            if (!string.IsNullOrWhiteSpace(textContent))
            {
                EmitStreamingTextUpdate(request.ConversationId, textContent, isComplete: true);
            }

            // Add model response (with function calls) to contents
            var modelParts = new List<object>();
            if (!string.IsNullOrWhiteSpace(textContent))
                modelParts.Add(new Dictionary<string, object> { ["text"] = textContent });

            foreach (var fc in functionCalls)
            {
                var argsObj = fc.Args.ValueKind != JsonValueKind.Undefined
                    ? (object)fc.Args
                    : new Dictionary<string, object>();

                modelParts.Add(new Dictionary<string, object>
                {
                    ["functionCall"] = new Dictionary<string, object>
                    {
                        ["name"] = fc.Name,
                        ["args"] = argsObj
                    }
                });
            }

            contents.Add(new Dictionary<string, object>
            {
                ["role"] = "model",
                ["parts"] = modelParts
            });

            // Execute each function call
            var functionResponseParts = new List<object>();
            var shouldStop = false;
            var stopForPlanGate = false;

            foreach (var funcCall in functionCalls)
            {
                var callId = $"call_{Guid.NewGuid():N}";
                var argsJson = funcCall.Args.ValueKind != JsonValueKind.Undefined
                    ? JsonSerializer.Serialize(funcCall.Args)
                    : "{}";

                EmitToolCallStarted(request.ConversationId, callId, funcCall.Name, argsJson);

                var toolResult = await executeToolAsync(callId, funcCall.Name, argsJson);

                if (funcCall.Name == "propose_plan" && !stopForPlanGate)
                {
                    try
                    {
                        var doc = JsonDocument.Parse(toolResult);
                        if (doc.RootElement.TryGetProperty("awaitingApproval", out var aw) &&
                            aw.ValueKind == JsonValueKind.True)
                        {
                            stopForPlanGate = true;
                        }
                    }
                    catch { /* ignore JSON parse errors */ }
                }

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

            // Add tool results to contents as a user turn
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

            if (stopForPlanGate)
                break;
        }

        return responseBuilder.ToString().Trim();
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
                // Gemini uses "user" and "model" roles
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

    private async Task<JsonElement> SendGenerateContentAsync(
        string model,
        string apiKey,
        string? systemPrompt,
        List<Dictionary<string, object>> contents,
        List<ProviderTool> tools,
        CancellationToken cancellationToken)
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
            var functionDeclarations = tools.Select(tool =>
            {
                var schemaNode = BuildParameterSchema(tool.InputSchemaJson);
                return new Dictionary<string, object?>
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = schemaNode
                };
            }).ToList();

            requestBody["tools"] = new List<object>
            {
                new Dictionary<string, object> { ["function_declarations"] = functionDeclarations }
            };
            requestBody["tool_config"] = new Dictionary<string, object>
            {
                ["function_calling_config"] = new Dictionary<string, object> { ["mode"] = "AUTO" }
            };
        }

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = $"{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        using var responseMessage = await GeminiHttpClient.PostAsync(url, content, cancellationToken);
        var responseBody = await responseMessage.Content.ReadAsStringAsync(cancellationToken);

        if (!responseMessage.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini API error: {Status} {Body}", responseMessage.StatusCode, responseBody);

            string errorDetail = responseBody;
            try
            {
                using var errDoc = JsonDocument.Parse(responseBody);
                if (errDoc.RootElement.TryGetProperty("error", out var errEl) &&
                    errEl.TryGetProperty("message", out var msgEl))
                {
                    errorDetail = msgEl.GetString() ?? responseBody;
                }
            }
            catch { /* fall back to raw body */ }

            throw new InvalidOperationException($"Gemini API error: {responseMessage.StatusCode} - {errorDetail}");
        }

        using var responseDoc = JsonDocument.Parse(responseBody);
        return responseDoc.RootElement.Clone();
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
            // Gemini requires the schema to have type: "object" at root
            using var doc = JsonDocument.Parse(inputSchemaJson);
            var root = doc.RootElement;

            // Return as a raw JsonElement for proper serialization
            return root.Clone();
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
                new ToolCall
                {
                    Id = toolCallId,
                    Name = toolName,
                    Arguments = ParseToolArguments(argsJson)
                }
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

    private class GeminiFunctionCall
    {
        public string Name { get; set; } = string.Empty;
        public JsonElement Args { get; set; }
    }
}
