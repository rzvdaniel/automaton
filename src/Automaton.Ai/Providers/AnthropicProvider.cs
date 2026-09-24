using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tool = Anthropic.SDK.Common.Tool;

namespace Automaton.Ai.Providers;

/// <summary>
/// Anthropic Claude provider implementation using the Anthropic.SDK package
/// </summary>
internal sealed class AnthropicConversationWorkflow
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger;
    private readonly AnthropicStreamResponseProcessor streamResponseProcessor;
    private readonly AnthropicConversationEventEmitter eventEmitter;
    private readonly AnthropicMessageFactory messageFactory;
    private readonly AnthropicTurnExecutor turnExecutor;

    /// <summary>
    /// Maximum number of conversation messages to keep in history (sliding window).
    /// Prevents unbounded token growth. Each turn = 2 messages (user + assistant).
    /// 10 messages = last 5 turns, typically 2,000-5,000 tokens saved per call.
    /// </summary>
    private const int MaxConversationHistoryMessages = 10;
    /// <summary>
    /// Fallback output token cap used when no model-specific MaxTokens is configured.
    /// 8192 is within the output limit of all current Claude models.
    /// </summary>
    private const string DefaultModelName = Anthropic.SDK.Constants.AnthropicModels.Claude35Sonnet;

    public AnthropicConversationWorkflow(
        Microsoft.Extensions.Logging.ILogger logger,
        Action<ConversationUpdatedEvent> publish)
    {
        _logger = logger;
        eventEmitter = new AnthropicConversationEventEmitter(_logger, publish);
        messageFactory = new AnthropicMessageFactory(_logger);
        streamResponseProcessor = new AnthropicStreamResponseProcessor(_logger, eventEmitter.EmitStreamingTextUpdate);
        turnExecutor = new AnthropicTurnExecutor(_logger, streamResponseProcessor, eventEmitter);
    }

    public string DefaultModel => DefaultModelName;

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
            _logger.LogDebug("Anthropic conversation cancelled by client");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Anthropic API with MCP tools");
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
        var client = turnExecutor.CreateClient(request.ApiKey);

        // Build the initial messages
        var messages = messageFactory.BuildMessages(request);

        // Build the tools list
        var anthropicTools = messageFactory.BuildTools(tools);

        // Handle iterative conversation with tool calls
        var responseBuilder = new StringBuilder();

        // Safety limits to prevent infinite loops (from user settings or defaults)
        var maxIterations = ProviderIterationLimits.Resolve(request.MaxIterations);
        var maxConsecutiveFailures = request.MaxConsecutiveFailures > 0 ? request.MaxConsecutiveFailures : 3;
        var iterationCount = 0;
        var consecutiveFailures = new Dictionary<string, int>(); // Track failures per tool

        // Accumulate billed totals across iterations and emit once on the final turn.
        var tokenUsage = new TokenUsageAccumulator();

        // When propose_plan returns awaitingApproval, suppress tools on the next iteration
        // so the LLM can present the plan and ask for user confirmation.
        var breakAfterPlanPresentation = false;

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
            var streamResult = await turnExecutor.ExecuteAsync(
                client, request, messages, anthropicTools, breakAfterPlanPresentation,
                tokenUsage, cancellationToken);

            // Accumulate tokens across all iterations — input, output, and cache are all per-call values.
            tokenUsage.Add(
                streamResult.InputTokens,
                streamResult.OutputTokens,
                cacheReadTokens: streamResult.CacheReadTokens,
                cacheCreationTokens: streamResult.CacheCreationTokens);

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

            AddAssistantToolUseMessage(messages, streamResult);
            var execution = await ExecuteToolUseBlocksAsync(
                request.ConversationId, streamResult.ToolUseBlocks, executeToolAsync);
            var toolOutcome = ProcessToolExecutionResults(
                execution, consecutiveFailures, maxConsecutiveFailures);

            // If we hit max failures, add a message and break
            if (toolOutcome.ShouldStopDueToFailures)
            {
                responseBuilder.AppendLine("\n\n[Conversation stopped: Tool repeatedly failed]");
                // Still add the tool results so the AI sees the final error
                messages.Add(new Message
                {
                    Role = RoleType.User,
                    Content = toolOutcome.Results
                });
                break;
            }

            // Add user message with tool results
            messages.Add(new Message
            {
                Role = RoleType.User,
                Content = toolOutcome.Results
            });

            if (toolOutcome.ShouldBreakForApproval)
            {
                breakAfterPlanPresentation = true;
                continue;
            }
        }

        return new ProviderResult(responseBuilder.ToString().Trim(), iterationCount);
    }

    private static void AddAssistantToolUseMessage(List<Message> messages, AnthropicStreamResponseProcessor.StreamResult result)
    {
        var content = new List<ContentBase>();
        if (!string.IsNullOrWhiteSpace(result.TextContent))
            content.Add(new TextContent { Text = result.TextContent });

        foreach (var block in result.ToolUseBlocks)
        {
            content.Add(new ToolUseContent
            {
                Id = block.Id,
                Name = block.Name,
                Input = block.Input ?? JsonNode.Parse("{}")
            });
        }

        messages.Add(new Message { Role = RoleType.Assistant, Content = content });
    }

    private async Task<IReadOnlyList<ExecutedToolResult>> ExecuteToolUseBlocksAsync(
        string conversationId,
        IReadOnlyList<AnthropicStreamResponseProcessor.ToolUseBlockInfo> toolUseBlocks,
        Func<string, string, string, Task<string>> executeToolAsync)
    {
        var tasks = toolUseBlocks.Select(async toolUse =>
        {
            var argsJson = toolUse.Input is null ? "{}" : JsonSerializer.Serialize(toolUse.Input);
            eventEmitter.EmitToolCallStarted(conversationId, toolUse.Id, toolUse.Name, argsJson);
            var result = await executeToolAsync(toolUse.Id, toolUse.Name, argsJson);
            eventEmitter.EmitToolCallCompleted(conversationId, toolUse.Id, toolUse.Name, argsJson, result);
            return new ExecutedToolResult(toolUse.Id, toolUse.Name, argsJson, result);
        });

        return await Task.WhenAll(tasks);
    }

    private ToolExecutionOutcome ProcessToolExecutionResults(
        IReadOnlyList<ExecutedToolResult> executions,
        Dictionary<string, int> consecutiveFailures,
        int maxConsecutiveFailures)
    {
        var results = new List<ContentBase>(executions.Count);
        var shouldStop = false;
        var shouldBreakForApproval = false;
        foreach (var execution in executions)
        {
            shouldStop |= UpdateFailureCount(execution, consecutiveFailures, maxConsecutiveFailures);
            shouldBreakForApproval |= ProviderHelpers.ToolResultAwaitsApproval(execution.Name, execution.Result);
            results.Add(new ToolResultContent
            {
                ToolUseId = execution.Id,
                Content = new List<ContentBase> { new TextContent { Text = execution.Result } }
            });
        }

        return new ToolExecutionOutcome(results, shouldStop, shouldBreakForApproval);
    }

    private bool UpdateFailureCount(
        ExecutedToolResult execution,
        Dictionary<string, int> consecutiveFailures,
        int maxConsecutiveFailures)
    {
        if (!IsToolExecutionFailure(execution.Result))
        {
            consecutiveFailures[execution.Name] = 0;
            return false;
        }

        consecutiveFailures.TryGetValue(execution.Name, out var currentFailures);
        var failureCount = consecutiveFailures[execution.Name] = currentFailures + 1;
        if (failureCount < maxConsecutiveFailures)
            return false;

        _logger.LogWarning(
            "Tool {ToolName} failed {FailCount} consecutive times, stopping conversation",
            execution.Name, failureCount);
        return true;
    }

    private static bool IsToolExecutionFailure(string result) =>
        result.StartsWith("Tool execution failed:") ||
        result.StartsWith("Error executing tool") ||
        result.Contains("A second operation was started");

    private sealed record ExecutedToolResult(string Id, string Name, string ArgumentsJson, string Result);

    private sealed record ToolExecutionOutcome(
        List<ContentBase> Results,
        bool ShouldStopDueToFailures,
        bool ShouldBreakForApproval);

}
