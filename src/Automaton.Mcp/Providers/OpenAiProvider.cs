using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Automaton.Mcp.Client.Providers;

/// <summary>
/// OpenAI provider implementation using the official OpenAI .NET SDK
/// </summary>
public class OpenAiProvider : IAiProvider
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger;

    /// <summary>
    /// Maximum number of conversation messages to keep in history (sliding window).
    /// Prevents unbounded token growth. Each turn = 2 messages (user + assistant).
    /// 10 messages = last 5 turns, typically 2,000-5,000 tokens saved per call.
    /// </summary>
    private const int MaxConversationHistoryMessages = 10;

    /// <inheritdoc />
    public string ProviderName => "OpenAI";

    /// <inheritdoc />
    public string DefaultModel => "gpt-4o";

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    public OpenAiProvider(Microsoft.Extensions.Logging.ILogger logger)
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
            return await CallResponsesAsync(request, tools, executeToolAsync, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("OpenAI conversation cancelled by client");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI API with MCP tools");

            // Emit error as conversation update
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

    private void EmitToolCallStarted(string conversationId, string toolCallId, string toolName, string argsJson)
    {
        var currentToolCalls = new List<ToolCall>
        {
            new ToolCall
            {
                Id = toolCallId,
                Name = toolName,
                Arguments = ParseToolArguments(argsJson)
            }
        };

        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = new List<ConversationMessage>(),
            ToolCalls = currentToolCalls,
            ToolResults = new List<ToolResult>()
        });
    }

    private void EmitToolCallCompleted(string conversationId, string toolCallId, string toolName, string argsJson, string result)
    {
        var currentToolCalls = new List<ToolCall>
        {
            new ToolCall
            {
                Id = toolCallId,
                Name = toolName,
                Arguments = ParseToolArguments(argsJson)
            }
        };

        var toolResultObj = new ToolResult
        {
            ToolCallId = toolCallId,
            Result = result,
            IsSuccess = !result.StartsWith("Tool execution failed:") && !result.StartsWith("Error executing tool"),
            ErrorMessage = result.StartsWith("Tool execution failed:") || result.StartsWith("Error executing tool") ? result : null
        };

        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = new List<ConversationMessage>(),
            ToolCalls = currentToolCalls,
            ToolResults = new List<ToolResult> { toolResultObj }
        });
    }

    private Dictionary<string, object?> ParseToolArguments(string argsJson)
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

    private void EmitStreamingTextUpdate(string conversationId, string text, bool isComplete, int? inputTokens = null, int? outputTokens = null, string? model = null)
    {
        try
        {
            if (string.IsNullOrEmpty(conversationId))
            {
                _logger.LogWarning("EmitStreamingTextUpdate called with null/empty conversationId");
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                _logger.LogDebug("EmitStreamingTextUpdate called with null/empty text, skipping");
                return;
            }

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in EmitStreamingTextUpdate: {Message}", ex.Message);
            throw;
        }
    }

    private static readonly HttpClient ResponsesHttpClient = new()
    {
        BaseAddress = new Uri("https://api.openai.com/v1/"),
        Timeout     = System.Threading.Timeout.InfiniteTimeSpan
    };

    private async Task<ProviderResult> CallResponsesAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        var inputItems = new List<object>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            inputItems.Add(CreateInputMessage("system", request.SystemPrompt));
        }

        if (request.ConversationHistory?.Any() == true)
        {
            var historyToInclude = request.ConversationHistory;

            if (request.ConversationHistory.Count > MaxConversationHistoryMessages)
            {
                var skipCount = request.ConversationHistory.Count - MaxConversationHistoryMessages;
                historyToInclude = request.ConversationHistory.Skip(skipCount).ToList();

                _logger.LogInformation(
                    "Conversation history truncated: {TotalMessages} messages -> {KeptMessages} messages (sliding window)",
                    request.ConversationHistory.Count,
                    historyToInclude.Count);
            }

            foreach (var msg in historyToInclude)
            {
                var role = msg.Role.ToLowerInvariant() switch
                {
                    "assistant" => "assistant",
                    "user" => "user",
                    "system" => "system",
                    _ => "user"
                };

                inputItems.Add(CreateInputMessage(role, msg.Content));
            }
        }

        inputItems.Add(CreateInputMessage("user", request.InitialMessage));

        var toolSchemaDocs = new List<JsonDocument>();
        var toolDefinitions = tools.Select(tool =>
        {
            var schemaDoc = JsonDocument.Parse(tool.InputSchemaJson);
            toolSchemaDocs.Add(schemaDoc);

            return new Dictionary<string, object?>
            {
                ["type"] = "function",
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = schemaDoc.RootElement
            };
        }).ToList();

        var responseBuilder = new StringBuilder();
        var maxIterations = request.MaxIterations > 0 ? request.MaxIterations : 20;
        var maxConsecutiveFailures = request.MaxConsecutiveFailures > 0 ? request.MaxConsecutiveFailures : 3;
        var iterationCount = 0;
        var consecutiveFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Accumulate tokens across iterations.
        // OpenAI reports inputTokens cumulatively — keep the latest value.
        // outputTokens is per-call — sum across iterations.
        int? totalInputTokens = null;
        int totalOutputTokens = 0;

        try
        {
            while (true)
            {
                iterationCount++;
                if (iterationCount > maxIterations)
                {
                    _logger.LogWarning("Conversation exceeded maximum iterations ({MaxIterations}), stopping", maxIterations);
                    responseBuilder.AppendLine("\n\n[Conversation stopped: Maximum tool call iterations reached]");
                    break;
                }

                var response = await CreateResponsesAsync(
                    request.Model ?? DefaultModel,
                    request.ApiKey,
                    inputItems,
                    toolDefinitions,
                    cancellationToken);

                // Accumulate tokens across iterations.
                // OpenAI reports inputTokens cumulatively — keep the latest value.
                // outputTokens is per-call — sum across iterations.
                if (response.InputTokens.HasValue) totalInputTokens = response.InputTokens;
                if (response.OutputTokens.HasValue) totalOutputTokens += response.OutputTokens.Value;

                if (response.ToolCalls.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(response.OutputText))
                    {
                        EmitStreamingTextUpdate(request.ConversationId, response.OutputText, isComplete: true,
                            inputTokens: totalInputTokens, outputTokens: totalOutputTokens > 0 ? totalOutputTokens : null, model: response.Model);
                        responseBuilder.AppendLine(response.OutputText);
                    }

                    break;
                }

                if (!string.IsNullOrWhiteSpace(response.OutputText))
                {
                    EmitStreamingTextUpdate(request.ConversationId, response.OutputText, isComplete: true,
                        inputTokens: totalInputTokens, outputTokens: totalOutputTokens > 0 ? totalOutputTokens : null, model: response.Model);
                }

                var reusableItems = FilterOutputItemsForInput(response.OutputItems);
                inputItems.AddRange(reusableItems);

                var shouldStopDueToFailures = false;
                var shouldBreakForApproval  = false;
                foreach (var toolCall in response.ToolCalls)
                {
                    var argsJson = string.IsNullOrEmpty(toolCall.ArgumentsJson) ? "{}" : toolCall.ArgumentsJson;

                    EmitToolCallStarted(request.ConversationId, toolCall.Id, toolCall.Name, argsJson);

                    var toolResult = await executeToolAsync(toolCall.Id, toolCall.Name, argsJson);

                    var isFailure = toolResult.StartsWith("Tool execution failed:") ||
                                    toolResult.StartsWith("Error executing tool") ||
                                    toolResult.Contains("A second operation was started");

                    if (isFailure)
                    {
                        consecutiveFailures.TryGetValue(toolCall.Name, out var currentFailures);
                        consecutiveFailures[toolCall.Name] = currentFailures + 1;

                        if (consecutiveFailures[toolCall.Name] >= maxConsecutiveFailures)
                        {
                            _logger.LogWarning(
                                "Tool {ToolName} failed {FailCount} consecutive times, stopping conversation",
                                toolCall.Name, consecutiveFailures[toolCall.Name]);

                            toolResult += $"\n\n[CRITICAL: This tool has failed {consecutiveFailures[toolCall.Name]} times consecutively. " +
                                          "Please stop calling this tool and inform the user about the issue.]";
                            shouldStopDueToFailures = true;
                        }
                    }
                    else
                    {
                        consecutiveFailures[toolCall.Name] = 0;
                    }

                    EmitToolCallCompleted(request.ConversationId, toolCall.Id, toolCall.Name, argsJson, toolResult);

                    if (ProviderHelpers.ToolResultAwaitsApproval(toolCall.Name, toolResult))
                        shouldBreakForApproval = true; // reuse the existing break flag

                    inputItems.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "function_call_output",
                        ["call_id"] = toolCall.CallId,
                        ["output"] = toolResult
                    });
                }

                if (shouldStopDueToFailures)
                {
                    responseBuilder.AppendLine("\n\n[Conversation stopped: Tool repeatedly failed]");
                    break;
                }

                if (shouldBreakForApproval)
                    break;
            }
        }
        finally
        {
            foreach (var doc in toolSchemaDocs)
            {
                doc.Dispose();
            }
        }

        return new ProviderResult(responseBuilder.ToString().Trim(), iterationCount);
    }

    private static Dictionary<string, object> CreateInputMessage(string role, string content)
    {
        return new Dictionary<string, object>
        {
            ["role"] = role,
            ["content"] = content
        };
    }

    private async Task<ResponsesApiResult> CreateResponsesAsync(
        string model,
        string apiKey,
        List<object> input,
        List<Dictionary<string, object?>> tools,
        CancellationToken cancellationToken)
    {
        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["input"] = input,
            ["tools"] = tools,
            ["tool_choice"] = tools.Count > 0 ? "auto" : "none"
        };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var responseMessage = await ResponsesHttpClient.SendAsync(requestMessage, cancellationToken);
        var responseContent = await responseMessage.Content.ReadAsStringAsync(cancellationToken);

        if (!responseMessage.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI responses API error: {Status} {Body}", responseMessage.StatusCode, responseContent);

            // Try to extract a meaningful error message from the response body
            string errorDetail = responseContent;
            try
            {
                using var errDoc = JsonDocument.Parse(responseContent);
                if (errDoc.RootElement.TryGetProperty("error", out var errEl) &&
                    errEl.TryGetProperty("message", out var msgEl))
                {
                    errorDetail = msgEl.GetString() ?? responseContent;
                }
            }
            catch { /* fall back to raw body */ }

            throw new InvalidOperationException(
                $"OpenAI responses API error: {responseMessage.StatusCode} - {errorDetail}");
        }

        using var responseDoc = JsonDocument.Parse(responseContent);
        var root = responseDoc.RootElement;

        var outputItems = new List<JsonElement>();
        if (root.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in outputElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogWarning("Skipping non-object output item with kind {Kind}", item.ValueKind);
                    continue;
                }

                outputItems.Add(item.Clone());
            }
        }

        var toolCalls = new List<ResponseToolCall>();
        foreach (var item in outputItems)
        {
            if (!item.TryGetProperty("type", out var typeElement))
            {
                continue;
            }

            if (!string.Equals(typeElement.GetString(), "function_call", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var arguments = item.TryGetProperty("arguments", out var argsElement) ? argsElement.GetString() : null;
            var callId = item.TryGetProperty("call_id", out var callIdElement) ? callIdElement.GetString() : null;
            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(callId) && !string.IsNullOrEmpty(id))
            {
                toolCalls.Add(new ResponseToolCall
                {
                    Id = id,
                    CallId = callId,
                    Name = name,
                    ArgumentsJson = arguments ?? "{}"
                });
            }
        }

        var outputText = root.TryGetProperty("output_text", out var outputTextElement)
            ? outputTextElement.GetString()
            : ExtractOutputText(outputItems);

        // Extract token usage from API response
        int? inputTokens = null;
        int? outputTokens = null;
        string? responseModel = model;  // Use the provided model as default

        if (root.TryGetProperty("usage", out var usageElement))
        {
            if (usageElement.TryGetProperty("input_tokens", out var inputTokensElement))
            {
                inputTokens = inputTokensElement.GetInt32();
            }

            if (usageElement.TryGetProperty("output_tokens", out var outputTokensElement))
            {
                outputTokens = outputTokensElement.GetInt32();
            }
        }

        // Extract model information from response if available
        if (root.TryGetProperty("model", out var modelElement))
        {
            responseModel = modelElement.GetString();
        }

        return new ResponsesApiResult
        {
            OutputText = outputText ?? string.Empty,
            OutputItems = outputItems,
            ToolCalls = toolCalls,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Model = responseModel
        };
    }

    private static string ExtractOutputText(List<JsonElement> outputItems)
    {
        var builder = new StringBuilder();

        foreach (var item in outputItems)
        {
            if (!item.TryGetProperty("type", out var typeElement))
            {
                continue;
            }

            if (!string.Equals(typeElement.GetString(), "message", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!item.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentPart in contentElement.EnumerateArray())
            {
                if (!contentPart.TryGetProperty("type", out var contentTypeElement))
                {
                    continue;
                }

                if (!string.Equals(contentTypeElement.GetString(), "output_text", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (contentPart.TryGetProperty("text", out var textElement))
                {
                    builder.Append(textElement.GetString());
                }
            }
        }

        return builder.ToString();
    }

    private List<object> FilterOutputItemsForInput(List<JsonElement> outputItems)
    {
        // All output items must be returned as-is to preserve ordering constraints.
        // Reasoning models (e.g. o-series, gpt-5-mini) produce 'reasoning' items that
        // must be immediately followed by their associated item in the next request.
        // Filtering any item out breaks that contract and causes a BadRequest from the API.
        return outputItems.Cast<object>().ToList();
    }

    private sealed class ResponsesApiResult
    {
        public string OutputText { get; set; } = string.Empty;
        public List<JsonElement> OutputItems { get; set; } = new();
        public List<ResponseToolCall> ToolCalls { get; set; } = new();
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public string? Model { get; set; }
    }

    private sealed class ResponseToolCall
    {
        public string Id { get; set; } = string.Empty;
        public string CallId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ArgumentsJson { get; set; } = "{}";
    }
}
