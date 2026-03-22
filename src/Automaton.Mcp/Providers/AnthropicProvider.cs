using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Automaton.Mcp.Models;
using Automaton.Mcp.SystemPrompts;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tool = Anthropic.SDK.Common.Tool;

namespace Automaton.Mcp.Client.Providers;

/// <summary>
/// Anthropic Claude provider implementation using the Anthropic.SDK package
/// </summary>
public class AnthropicProvider : IAiProvider
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger;

    /// <summary>
    /// Maximum number of conversation messages to keep in history (sliding window).
    /// Prevents unbounded token growth. Each turn = 2 messages (user + assistant).
    /// 10 messages = last 5 turns, typically 2,000-5,000 tokens saved per call.
    /// </summary>
    private const int MaxConversationHistoryMessages = 10;

    /// <inheritdoc />
    public string ProviderName => "Anthropic";

    /// <inheritdoc />
    public string DefaultModel => Anthropic.SDK.Constants.AnthropicModels.Claude35Sonnet;

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    public AnthropicProvider(Microsoft.Extensions.Logging.ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Shared HttpClient with infinite timeout for streaming AI calls.
    /// CancellationToken controls actual request lifetime — not the client timeout.
    /// </summary>
    private static readonly HttpClient StreamingHttpClient = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    private static readonly System.Reflection.PropertyInfo? AnthropicHttpClientProperty =
        typeof(AnthropicClient).GetProperty(
            "HttpClient",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    public async Task<ProviderResult> CallAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CallAnthropicAsync(request, tools, executeToolAsync, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Anthropic conversation cancelled by client");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Anthropic API with MCP tools");

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

    private async Task<ProviderResult> CallAnthropicAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        // Create Anthropic client with API key, then inject our streaming HttpClient
        // so that long-running streaming calls don't hit the default 100-second timeout.
        var client = new AnthropicClient(request.ApiKey);
        AnthropicHttpClientProperty?.SetValue(client, StreamingHttpClient);

        // Build the initial messages
        var messages = BuildMessages(request);

        // Build the tools list
        var anthropicTools = BuildTools(tools);

        // Handle iterative conversation with tool calls
        var responseBuilder = new StringBuilder();

        // Safety limits to prevent infinite loops (from user settings or defaults)
        var maxIterations = IterationLimits.Resolve(null, request.MaxIterations);
        var maxConsecutiveFailures = request.MaxConsecutiveFailures > 0 ? request.MaxConsecutiveFailures : 3;
        var iterationCount = 0;
        var consecutiveFailures = new Dictionary<string, int>(); // Track failures per tool

        // Accumulate tokens across iterations.
        // Anthropic reports inputTokens cumulatively — keep the latest value.
        // outputTokens is per-call — sum across iterations.
        int? totalInputTokens = null;
        int totalOutputTokens = 0;

        while (true)
        {
            // Check iteration limit
            iterationCount++;
            if (iterationCount > maxIterations)
            {
                _logger.LogWarning("Conversation exceeded maximum iterations ({MaxIterations}), stopping", maxIterations);
                responseBuilder.AppendLine("\n\n[Conversation stopped: Maximum tool call iterations reached]");
                break;
            }
            // Create the message parameters
            var messageParams = new MessageParameters
            {
                Model = request.Model ?? DefaultModel,
                MaxTokens = 4096,
                System = string.IsNullOrEmpty(request.SystemPrompt) ? null : new List<SystemMessage> { new SystemMessage(request.SystemPrompt) },
                Messages = messages,
                Tools = anthropicTools.Count > 0 ? anthropicTools : null,
                Stream = true
            };

            // Stream the response
            var streamResult = await StreamResponseAsync(
                client, messageParams, request.ConversationId, cancellationToken,
                accumulatedInputTokens: totalInputTokens,
                accumulatedOutputTokens: totalOutputTokens);

            // Accumulate tokens across iterations.
            // Anthropic reports inputTokens cumulatively — keep the latest value.
            // outputTokens is per-call — sum across iterations.
            if (streamResult.InputTokens.HasValue) totalInputTokens = streamResult.InputTokens;
            if (streamResult.OutputTokens.HasValue) totalOutputTokens += streamResult.OutputTokens.Value;

            // If there's text content, add it to the response
            if (!string.IsNullOrWhiteSpace(streamResult.TextContent))
            {
                responseBuilder.AppendLine(streamResult.TextContent);
            }

            // If the model stopped normally (no tool use), we're done
            if (streamResult.StopReason != "tool_use" || !streamResult.ToolUseBlocks.Any())
            {
                break;
            }

            // Add assistant message with tool use blocks to conversation
            var assistantContent = new List<ContentBase>();

            if (!string.IsNullOrWhiteSpace(streamResult.TextContent))
            {
                assistantContent.Add(new TextContent { Text = streamResult.TextContent });
            }

            foreach (var toolUse in streamResult.ToolUseBlocks)
            {
                assistantContent.Add(new ToolUseContent
                {
                    Id = toolUse.Id,
                    Name = toolUse.Name,
                    // Anthropic API requires Input to be non-null; use empty object if null
                    Input = toolUse.Input ?? JsonNode.Parse("{}")
                });
            }

            messages.Add(new Message
            {
                Role = RoleType.Assistant,
                Content = assistantContent
            });

            // Execute all tool calls concurrently — results are order-independent
            var toolResultsList = new List<ContentBase>();
            var shouldStopDueToFailures = false;
            var shouldBreakForApproval  = false;

            var toolTasks = streamResult.ToolUseBlocks.Select(async toolUse =>
            {
                var argsJson = toolUse.Input != null ? JsonSerializer.Serialize(toolUse.Input) : "{}";
                EmitToolCallStarted(request.ConversationId, toolUse.Id, toolUse.Name, argsJson);
                var toolResult = await executeToolAsync(toolUse.Id, toolUse.Name, argsJson);
                EmitToolCallCompleted(request.ConversationId, toolUse.Id, toolUse.Name, argsJson, toolResult);
                return (toolUse, argsJson, toolResult);
            });

            var toolExecutionResults = await Task.WhenAll(toolTasks);

            foreach (var (toolUse, argsJson, toolResult) in toolExecutionResults)
            {
                var isFailure = toolResult.StartsWith("Tool execution failed:") ||
                               toolResult.StartsWith("Error executing tool") ||
                               toolResult.Contains("A second operation was started");

                if (isFailure)
                {
                    consecutiveFailures.TryGetValue(toolUse.Name, out var currentFailures);
                    consecutiveFailures[toolUse.Name] = currentFailures + 1;

                    if (consecutiveFailures[toolUse.Name] >= maxConsecutiveFailures)
                    {
                        _logger.LogWarning(
                            "Tool {ToolName} failed {FailCount} consecutive times, stopping conversation",
                            toolUse.Name, consecutiveFailures[toolUse.Name]);
                        shouldStopDueToFailures = true;
                    }
                }
                else
                {
                    consecutiveFailures[toolUse.Name] = 0;
                }

                if (ProviderHelpers.ToolResultAwaitsApproval(toolUse.Name, toolResult))
                    shouldBreakForApproval = true;

                toolResultsList.Add(new ToolResultContent
                {
                    ToolUseId = toolUse.Id,
                    Content   = new List<ContentBase> { new TextContent { Text = toolResult } }
                });
            }

            var toolResults = toolResultsList;

            // If we hit max failures, add a message and break
            if (shouldStopDueToFailures)
            {
                responseBuilder.AppendLine("\n\n[Conversation stopped: Tool repeatedly failed]");
                // Still add the tool results so the AI sees the final error
                messages.Add(new Message
                {
                    Role = RoleType.User,
                    Content = toolResults
                });
                break;
            }

            // Add user message with tool results
            messages.Add(new Message
            {
                Role = RoleType.User,
                Content = toolResults
            });

            if (shouldBreakForApproval)
                break;
        }

        return new ProviderResult(responseBuilder.ToString().Trim(), iterationCount);
    }

    private List<Message> BuildMessages(ConversationRequest request)
    {
        var messages = new List<Message>();

        // Add conversation history with sliding window to prevent unbounded token growth
        if (request.ConversationHistory?.Any() == true)
        {
            var historyToInclude = request.ConversationHistory;

            // Apply sliding window: keep only last N messages
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
                var role = msg.Role.ToLowerInvariant() == "assistant" ? RoleType.Assistant : RoleType.User;
                messages.Add(new Message
                {
                    Role = role,
                    Content = new List<ContentBase> { new TextContent { Text = msg.Content } }
                });
            }
        }

        // Add current user message
        messages.Add(new Message
        {
            Role = RoleType.User,
            Content = new List<ContentBase> { new TextContent { Text = request.InitialMessage } }
        });

        return messages;
    }

    private List<Tool> BuildTools(IEnumerable<ProviderTool> tools)
    {
        var anthropicTools = new List<Tool>();

        foreach (var tool in tools)
        {
            try
            {
                // Parse the input schema JSON into a JsonNode for the Function
                JsonNode? schemaNode = null;

                if (!string.IsNullOrEmpty(tool.InputSchemaJson) && tool.InputSchemaJson != "{}")
                {
                    try
                    {
                        schemaNode = JsonNode.Parse(tool.InputSchemaJson);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse input schema for tool {ToolName}", tool.Name);
                        // Default to empty object schema
                        schemaNode = JsonNode.Parse("{\"type\":\"object\",\"properties\":{}}");
                    }
                }
                else
                {
                    schemaNode = JsonNode.Parse("{\"type\":\"object\",\"properties\":{}}");
                }

                anthropicTools.Add(new Tool(new Function(tool.Name, tool.Description, schemaNode)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create tool {ToolName}", tool.Name);
            }
        }

        return anthropicTools;
    }

    private async Task<StreamResult> StreamResponseAsync(
        AnthropicClient client,
        MessageParameters messageParams,
        string conversationId,
        CancellationToken cancellationToken,
        int? accumulatedInputTokens = null,
        int accumulatedOutputTokens = 0)
    {
        var result = new StreamResult { Model = messageParams.Model };
        var currentText = new StringBuilder();
        var toolUseBlocks = new Dictionary<int, ToolUseInfo>();

        // Track when we last emitted an update for throttling
        var lastEmitTime = DateTime.UtcNow;
        var emitIntervalMs = 100;

        try
        {
            var currentToolIndex = 0;

            await foreach (var streamEvent in client.Messages.StreamClaudeMessageAsync(messageParams, cancellationToken))
            {
                // Capture usage tokens if available (usually in final event)
                if (streamEvent.Usage != null)
                {
                    result.InputTokens = streamEvent.Usage.InputTokens;
                    result.OutputTokens = streamEvent.Usage.OutputTokens;
                    // Model is consistent across the stream, captured at start
                }

                // Handle text content
                if (streamEvent.Delta?.Text != null)
                {
                    currentText.Append(streamEvent.Delta.Text);

                    // Throttle updates
                    var now = DateTime.UtcNow;
                    if ((now - lastEmitTime).TotalMilliseconds >= emitIntervalMs)
                    {
                        EmitStreamingTextUpdate(conversationId, currentText.ToString(), isComplete: false);
                        lastEmitTime = now;
                    }
                }

                // Handle tool use content blocks
                if (streamEvent.ContentBlock != null)
                {
                    if (streamEvent.ContentBlock.Type == "tool_use")
                    {
                        toolUseBlocks[currentToolIndex] = new ToolUseInfo
                        {
                            Index = currentToolIndex,
                            Id = streamEvent.ContentBlock.Id ?? "",
                            Name = streamEvent.ContentBlock.Name ?? "",
                            InputBuilder = new StringBuilder()
                        };
                        currentToolIndex++;
                    }
                }

                // Handle tool input deltas
                if (streamEvent.Delta?.PartialJson != null)
                {
                    var lastToolIndex = currentToolIndex - 1;
                    if (lastToolIndex >= 0 && toolUseBlocks.TryGetValue(lastToolIndex, out var toolInfo))
                    {
                        toolInfo.InputBuilder.Append(streamEvent.Delta.PartialJson);
                    }
                }

                // Handle stop reason
                if (streamEvent.Delta?.StopReason != null)
                {
                    result.StopReason = streamEvent.Delta.StopReason;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Anthropic streaming: {Message}", ex.Message);
            throw;
        }

        // Finalize text content
        var finalText = currentText.ToString();
        if (!string.IsNullOrWhiteSpace(finalText))
        {
            // Use accumulated totals so multi-turn conversations report correct cumulative usage
            var emitInput = result.InputTokens.HasValue ? result.InputTokens : accumulatedInputTokens;
            var emitOutput = accumulatedOutputTokens + (result.OutputTokens ?? 0);
            EmitStreamingTextUpdate(conversationId, finalText, isComplete: true, inputTokens: emitInput, outputTokens: emitOutput > 0 ? emitOutput : null, model: result.Model);
            result.TextContent = finalText;
        }

        // Finalize tool use blocks
        foreach (var toolInfo in toolUseBlocks.Values)
        {
            var inputJson = toolInfo.InputBuilder.ToString();
            JsonNode? inputNode = null;

            if (!string.IsNullOrEmpty(inputJson))
            {
                try
                {
                    inputNode = JsonNode.Parse(inputJson);
                }
                catch
                {
                    _logger.LogWarning("Failed to parse tool input JSON: {Json}", inputJson);
                }
            }

            result.ToolUseBlocks.Add(new ToolUseBlockInfo
            {
                Id = toolInfo.Id,
                Name = toolInfo.Name,
                Input = inputNode
            });
        }

        return result;
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

    // Helper classes
    private class StreamResult
    {
        public string? TextContent { get; set; }
        public string? StopReason { get; set; }
        public List<ToolUseBlockInfo> ToolUseBlocks { get; } = new();
        
        // Token usage from API response
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public string? Model { get; set; }
    }

    private class ToolUseBlockInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public JsonNode? Input { get; set; }
    }

    private class ToolUseInfo
    {
        public int Index { get; set; }
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public StringBuilder InputBuilder { get; set; } = new();
    }
}
