using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Automaton.Ai.Providers;

internal sealed class GeminiToolCallExecutor
{
    private readonly ILogger _logger;
    private readonly GoogleConversationEventEmitter _eventEmitter;

    public GeminiToolCallExecutor(ILogger logger, GoogleConversationEventEmitter eventEmitter)
    {
        _logger = logger;
        _eventEmitter = eventEmitter;
    }

    public async Task<GeminiToolOutcome> ExecuteAsync(
        string conversationId,
        IReadOnlyList<GeminiFunctionCall> functionCalls,
        Func<string, string, string, Task<string>> executeToolAsync,
        Dictionary<string, int> consecutiveFailures,
        int maxConsecutiveFailures)
    {
        var tasks = functionCalls.Select(functionCall => ExecuteToolAsync(
            conversationId, functionCall, executeToolAsync));
        var executions = await Task.WhenAll(tasks);
        return BuildOutcome(executions, consecutiveFailures, maxConsecutiveFailures);
    }

    private async Task<GeminiToolExecution> ExecuteToolAsync(
        string conversationId,
        GeminiFunctionCall functionCall,
        Func<string, string, string, Task<string>> executeToolAsync)
    {
        var callId = $"call_{Guid.NewGuid():N}";
        var argsJson = functionCall.Args.ValueKind == JsonValueKind.Undefined
            ? "{}"
            : JsonSerializer.Serialize(functionCall.Args);
        _eventEmitter.EmitToolCallStarted(conversationId, callId, functionCall.Name, argsJson);
        var result = await executeToolAsync(callId, functionCall.Name, argsJson);
        _eventEmitter.EmitToolCallCompleted(conversationId, callId, functionCall.Name, argsJson, result);
        return new GeminiToolExecution(functionCall.Name, result);
    }

    private GeminiToolOutcome BuildOutcome(
        IReadOnlyList<GeminiToolExecution> executions,
        Dictionary<string, int> consecutiveFailures,
        int maxConsecutiveFailures)
    {
        var responseParts = new List<object>(executions.Count);
        var shouldStop = false;
        var shouldBreakForApproval = false;
        foreach (var execution in executions)
        {
            shouldStop |= UpdateConsecutiveFailures(execution, consecutiveFailures, maxConsecutiveFailures);
            shouldBreakForApproval |= ProviderHelpers.ToolResultAwaitsApproval(execution.Name, execution.Result);
            responseParts.Add(BuildFunctionResponsePart(execution.Name, execution.Result));
        }

        return new GeminiToolOutcome(responseParts, shouldStop, shouldBreakForApproval);
    }

    private bool UpdateConsecutiveFailures(
        GeminiToolExecution execution,
        Dictionary<string, int> consecutiveFailures,
        int maxConsecutiveFailures)
    {
        if (!IsToolFailure(execution.Result))
        {
            consecutiveFailures[execution.Name] = 0;
            return false;
        }

        consecutiveFailures.TryGetValue(execution.Name, out var current);
        var failureCount = consecutiveFailures[execution.Name] = current + 1;
        if (failureCount < maxConsecutiveFailures)
            return false;

        _logger.LogWarning(
            "Tool {ToolName} failed {FailCount} consecutive times, stopping",
            execution.Name, failureCount);
        return true;
    }

    private static bool IsToolFailure(string result) =>
        result.StartsWith("Tool execution failed:") ||
        result.StartsWith("Error executing tool") ||
        result.Contains("A second operation was started");

    private static Dictionary<string, object> BuildFunctionResponsePart(string name, string result) =>
        new()
        {
            ["functionResponse"] = new Dictionary<string, object>
            {
                ["name"] = name,
                ["response"] = new Dictionary<string, object> { ["result"] = result }
            }
        };
}

internal sealed record GeminiToolExecution(string Name, string Result);

internal sealed record GeminiToolOutcome(
    List<object> ResponseParts,
    bool ShouldStop,
    bool ShouldBreakForApproval);
