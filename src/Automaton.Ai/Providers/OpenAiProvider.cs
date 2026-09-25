using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Ai.Providers;

/// <summary>
/// OpenAI provider implementation using the official OpenAI .NET SDK
/// </summary>
public class OpenAiProvider : IAiProvider
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger;
    private readonly OpenAiResponsesApiClient _responsesApiClient;
    private readonly OpenAiConversationEventEmitter _eventEmitter;

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
        _responsesApiClient = new OpenAiResponsesApiClient(logger);
        _eventEmitter = new OpenAiConversationEventEmitter(logger, updated => ConversationUpdated?.Invoke(this, updated));
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
            _logger.LogDebug("OpenAI conversation cancelled by client");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI API with MCP tools");
            throw;
        }
    }

    private async Task<ProviderResult> CallResponsesAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        var inputItems = BuildInputItems(request);
        using var toolDefinitions = OpenAiToolDefinitionSet.Create(tools);

        var responseBuilder = new StringBuilder();
        var maxIterations = ProviderIterationLimits.Resolve(request.MaxIterations);
        var maxConsecutiveFailures = request.MaxConsecutiveFailures > 0 ? request.MaxConsecutiveFailures : 3;
        var iterationCount = 0;
        var consecutiveFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Accumulate billed totals across iterations and emit once on the final turn.
        var tokenUsage = new TokenUsageAccumulator();

        // When propose_plan returns awaitingApproval, suppress tools on the next iteration
        // so the LLM can present the plan and ask for user confirmation.
        var breakAfterPlanPresentation = false;

        while (true)
        {
            iterationCount++;
            if (iterationCount > maxIterations)
            {
                _logger.LogWarning("Conversation exceeded maximum iterations ({MaxIterations}), stopping", maxIterations);
                responseBuilder.AppendLine("\n\n[Conversation stopped: Maximum tool call iterations reached]");
                break;
            }

            var response = await ExecuteResponsesRequestAsync(
                request, inputItems, toolDefinitions.Definitions, breakAfterPlanPresentation, cancellationToken);
            tokenUsage.Add(response.InputTokens, response.OutputTokens);

            if (response.ToolCalls.Count == 0)
            {
                EmitFinalResponse(request, response, tokenUsage, responseBuilder);
                break;
            }

            _eventEmitter.EmitStreamingTextUpdate(request.ConversationId, response.OutputText, isComplete: true,
                inputTokens: null, outputTokens: null, model: response.Model);
            inputItems.AddRange(FilterOutputItemsForInput(response.OutputItems));
            var toolOutcome = await ExecuteResponseToolCallsAsync(
                request, response.ToolCalls, executeToolAsync, consecutiveFailures,
                maxConsecutiveFailures, inputItems);

            if (toolOutcome.ShouldStop)
            {
                responseBuilder.AppendLine("\n\n[Conversation stopped: Tool repeatedly failed]");
                break;
            }

            if (toolOutcome.ShouldBreakForApproval)
                breakAfterPlanPresentation = true;
        }

        return new ProviderResult(responseBuilder.ToString().Trim(), iterationCount);
    }

    private List<object> BuildInputItems(ConversationRequest request)
    {
        var inputItems = new List<object>();
        if (request.SystemPromptParts is not null)
        {
            inputItems.Add(CreateInputMessage("system", request.SystemPromptParts.BasePrompt));
            AddSystemPromptPart(inputItems, request.SystemPromptParts.InstructionBlock);
            AddSystemPromptPart(inputItems, request.SystemPromptParts.MemoryBlock);
        }
        else if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            inputItems.Add(CreateInputMessage("system", request.SystemPrompt));
        }

        AddConversationHistory(inputItems, request.ConversationHistory);
        inputItems.Add(CreateInputMessage("user", request.InitialMessage));
        return inputItems;
    }

    private static void AddSystemPromptPart(List<object> inputItems, string? promptPart)
    {
        if (!string.IsNullOrWhiteSpace(promptPart))
            inputItems.Add(CreateInputMessage("system", promptPart));
    }

    private void AddConversationHistory(List<object> inputItems, IReadOnlyList<ConversationMessage>? history)
    {
        if (history is not { Count: > 0 })
            return;

        var historyToInclude = history;
        if (history.Count > MaxConversationHistoryMessages)
        {
            historyToInclude = history.Skip(history.Count - MaxConversationHistoryMessages).ToList();
            _logger.LogDebug(
                "Conversation history truncated: {TotalMessages} messages -> {KeptMessages} messages (sliding window)",
                history.Count,
                historyToInclude.Count);
        }

        foreach (var message in historyToInclude)
            inputItems.Add(CreateInputMessage(ResolveHistoryRole(message.Role), message.Content));
    }

    private static string ResolveHistoryRole(string role) => role.ToLowerInvariant() switch
    {
        "assistant" => "assistant",
        "user" => "user",
        "system" => "system",
        _ => "user"
    };

    private async Task<OpenAiResponsesApiClient.ResponsesApiResult> ExecuteResponsesRequestAsync(
        ConversationRequest request,
        List<object> inputItems,
        List<Dictionary<string, object?>> toolDefinitions,
        bool breakAfterPlanPresentation,
        CancellationToken cancellationToken)
    {
        var retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = request.MaxRateLimitRetries,
                Delay = TimeSpan.FromSeconds(request.RateLimitRetryInitialDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(IsRateLimitException),
                DelayGenerator = static args =>
                    TryParseRetryAfter(args.Outcome.Exception, out var retryAfter)
                        ? ValueTask.FromResult<TimeSpan?>(retryAfter)
                        : ValueTask.FromResult<TimeSpan?>(null),
                OnRetry = args =>
                {
                    var delaySecs = (int)args.RetryDelay.TotalSeconds;
                    _logger.LogWarning(
                        "OpenAI rate limit hit (attempt {Retry}/{MaxRetries}). Model={Model}, Retrying in {Delay}s. Reason: {Error}",
                        args.AttemptNumber + 1, request.MaxRateLimitRetries,
                        request.Model, delaySecs, args.Outcome.Exception?.Message);
                    _eventEmitter.EmitRetryNotification(request.ConversationId, args.AttemptNumber + 1,
                        request.MaxRateLimitRetries, delaySecs);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        return await retryPipeline.ExecuteAsync(
            async ct => await _responsesApiClient.CreateResponsesAsync(
                request.Model ?? DefaultModel,
                request.ApiKey,
                request.MaxTokens,
                inputItems,
                breakAfterPlanPresentation ? [] : toolDefinitions,
                ct),
            cancellationToken);
    }

    private void EmitFinalResponse(
        ConversationRequest request,
        OpenAiResponsesApiClient.ResponsesApiResult response,
        TokenUsageAccumulator tokenUsage,
        StringBuilder responseBuilder)
    {
        if (!string.IsNullOrWhiteSpace(response.OutputText))
        {
            _eventEmitter.EmitStreamingTextUpdate(request.ConversationId, response.OutputText, isComplete: true,
                inputTokens: tokenUsage.InputTokens,
                outputTokens: tokenUsage.OutputTokensOrNull,
                model: response.Model);
            responseBuilder.AppendLine(response.OutputText);
        }
        else if (tokenUsage.HasAny)
        {
            _eventEmitter.EmitStreamingTextUpdate(request.ConversationId, string.Empty, isComplete: true,
                inputTokens: tokenUsage.InputTokens,
                outputTokens: tokenUsage.OutputTokensOrNull,
                model: response.Model);
        }
    }

    private async Task<ResponseToolOutcome> ExecuteResponseToolCallsAsync(
        ConversationRequest request,
        IReadOnlyList<OpenAiResponsesApiClient.ResponseToolCall> toolCalls,
        Func<string, string, string, Task<string>> executeToolAsync,
        Dictionary<string, int> consecutiveFailures,
        int maxConsecutiveFailures,
        List<object> inputItems)
    {
        var tasks = toolCalls.Select(async toolCall =>
        {
            var argsJson = string.IsNullOrEmpty(toolCall.ArgumentsJson) ? "{}" : toolCall.ArgumentsJson;
            _eventEmitter.EmitToolCallStarted(request.ConversationId, toolCall.Id, toolCall.Name, argsJson);
            var result = await executeToolAsync(toolCall.Id, toolCall.Name, argsJson);
            _eventEmitter.EmitToolCallCompleted(request.ConversationId, toolCall.Id, toolCall.Name, argsJson, result);
            return (toolCall, result);
        });

        var results = await Task.WhenAll(tasks);
        var shouldStop = false;
        var shouldBreakForApproval = false;
        foreach (var (toolCall, result) in results)
        {
            shouldStop |= UpdateConsecutiveFailures(toolCall.Name, result, consecutiveFailures, maxConsecutiveFailures);
            shouldBreakForApproval |= ProviderHelpers.ToolResultAwaitsApproval(toolCall.Name, result);
            inputItems.Add(new Dictionary<string, object?>
            {
                ["type"] = "function_call_output",
                ["call_id"] = toolCall.CallId,
                ["output"] = result
            });
        }

        return new ResponseToolOutcome(shouldStop, shouldBreakForApproval);
    }

    private bool UpdateConsecutiveFailures(
        string toolName,
        string result,
        Dictionary<string, int> consecutiveFailures,
        int maxConsecutiveFailures)
    {
        if (!IsToolFailure(result))
        {
            consecutiveFailures[toolName] = 0;
            return false;
        }

        consecutiveFailures.TryGetValue(toolName, out var currentFailures);
        consecutiveFailures[toolName] = currentFailures + 1;
        if (consecutiveFailures[toolName] < maxConsecutiveFailures)
            return false;

        _logger.LogWarning(
            "Tool {ToolName} failed {FailCount} consecutive times, stopping conversation",
            toolName, consecutiveFailures[toolName]);
        return true;
    }

    private static bool IsToolFailure(string result) =>
        result.StartsWith("Tool execution failed:") ||
        result.StartsWith("Error executing tool") ||
        result.Contains("A second operation was started");

    private sealed record ResponseToolOutcome(bool ShouldStop, bool ShouldBreakForApproval);

    private static Dictionary<string, object> CreateInputMessage(string role, string content)
    {
        return new Dictionary<string, object>
        {
            ["role"] = role,
            ["content"] = content
        };
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

        return msg.Contains("rate_limit",        StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("rate limited",      StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("429",               StringComparison.OrdinalIgnoreCase) ||
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

    private List<object> FilterOutputItemsForInput(List<JsonElement> outputItems)
    {
        // All output items must be returned as-is to preserve ordering constraints.
        // Reasoning models (e.g. o-series, gpt-5-mini) produce 'reasoning' items that
        // must be immediately followed by their associated item in the next request.
        // Filtering any item out breaks that contract and causes a BadRequest from the API.
        return outputItems.Cast<object>().ToList();
    }

}
