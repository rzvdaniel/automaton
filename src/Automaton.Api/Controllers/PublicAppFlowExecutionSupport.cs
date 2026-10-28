using Automaton.Api.Constants;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Api.Controllers;

internal static class PublicAppFlowExecutionSupport
{
    public static async Task<FlowExecutionAttempt> ExecuteAsync(
        FlowsService flowsService,
        RunnerService runnerService,
        string flowIdentifier,
        Guid ownerId,
        WebAppFlowExecutionRequest request,
        string? headerTestRunId,
        Guid? executionRunnerId,
        ILogger logger,
        bool isPreview,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveFlowAsync(
            flowsService, flowIdentifier, ownerId, logger, isPreview, cancellationToken);
        if (resolution.NotFoundMessage is not null)
        {
            return new FlowExecutionAttempt(null, resolution.NotFoundMessage, [], false, null, null);
        }

        var run = await RunAsync(
            flowsService,
            runnerService,
            resolution.Flow!,
            resolution.FlowId!.Value,
            request,
            headerTestRunId,
            executionRunnerId,
            cancellationToken);

        return new FlowExecutionAttempt(
            resolution.FlowId,
            null,
            run.MissingInputs,
            run.HasOnlineRunners,
            run.RunnerSelectionError,
            run.Response);
    }

    public static async Task<FlowRunOutcome> RunAsync(
        FlowsService flowsService,
        RunnerService runnerService,
        Flow flow,
        Guid flowId,
        WebAppFlowExecutionRequest request,
        string? headerTestRunId,
        Guid? executionRunnerId,
        CancellationToken cancellationToken)
    {
        var inputs = CreateInputs(request, headerTestRunId);
        var missingInputs = ApplyInputVariables(flow, inputs);
        var selection = await SelectRunnersAsync(
            runnerService,
            request.RunnerNames,
            executionRunnerId,
            cancellationToken);
        if (!selection.HasOnlineRunners)
        {
            return new FlowRunOutcome(
                missingInputs,
                HasOnlineRunners: false,
                RunnerSelectionError: null,
                Response: null);
        }

        if (selection.ErrorMessage is not null)
        {
            return new FlowRunOutcome(
                missingInputs,
                HasOnlineRunners: true,
                RunnerSelectionError: selection.ErrorMessage,
                Response: null);
        }

        var results = await flowsService.Execute(flowId, selection.RunnerIds, cancellationToken);
        return new FlowRunOutcome(
            missingInputs,
            HasOnlineRunners: true,
            null,
            CreateResponse(flowId, results));
    }

    public static async Task<FlowResolution> ResolveFlowAsync(
        FlowsService flowsService,
        string flowIdentifier,
        Guid ownerId,
        ILogger logger,
        bool isPreview,
        CancellationToken cancellationToken)
    {
        var flowId = await ResolveFlowIdAsync(flowsService, flowIdentifier, cancellationToken);
        if (flowId is null)
        {
            var message = $"Flow '{flowIdentifier}' not found";
            logger.LogWarning(
                isPreview ? "Preview flow name '{FlowName}' not found for owner {OwnerId}" : "Flow name '{FlowName}' not found for owner {OwnerId}",
                flowIdentifier,
                ownerId);
            return new FlowResolution(null, null, message);
        }

        var flow = flowsService.Get(flowId.Value);
        if (flow is null)
        {
            if (!isPreview)
            {
                logger.LogWarning("Flow not accessible: {FlowId}", flowId.Value);
            }

            return new FlowResolution(flowId, null, "Flow not found");
        }

        return new FlowResolution(flowId, flow, null);
    }

    public static async Task<RunnerSelection> SelectRunnersAsync(
        RunnerService runnerService,
        List<string>? requestedNames,
        Guid? executionRunnerId,
        CancellationToken cancellationToken)
    {
        var onlineRunners = (await runnerService.List(cancellationToken))
            .Where(runner => runner.Status == RunnerStatus.Online
                && RunnerProfileRules.IsValid(runner.Purpose, runner.ApplicationProfile))
            .ToList();
        if (onlineRunners.Count == 0)
        {
            return new RunnerSelection([], HasOnlineRunners: false, ErrorMessage: null);
        }

        try
        {
            return new RunnerSelection(
                SelectRunners(onlineRunners, requestedNames, executionRunnerId),
                HasOnlineRunners: true,
                ErrorMessage: null);
        }
        catch (InvalidOperationException exception)
        {
            return new RunnerSelection([], HasOnlineRunners: true, exception.Message);
        }
    }

    public static WebAppFlowExecutionResponse CreateResponse(Guid flowId, IEnumerable<RunnerFlowResult> results)
    {
        var firstResult = results.FirstOrDefault();
        var success = firstResult?.Status == WorkflowStatus.Success;
        return new WebAppFlowExecutionResponse
        {
            Success = success,
            FlowId = flowId,
            Status = firstResult?.Status.ToString() ?? "Unknown",
            RunnerCount = results.Count(),
            ExecutedAt = DateTime.UtcNow,
            Message = success ? "Flow executed successfully" : "Flow execution failed or incomplete",
            OutputVariables = firstResult?.OutputVariables ?? [],
            Logs = firstResult?.Logs
        };
    }

    public static async Task<Guid?> ResolveFlowIdAsync(
        FlowsService flowsService,
        string flowIdentifier,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(flowIdentifier, out var flowId))
        {
            return flowId;
        }

        return await flowsService.ResolveFlowNameToId(flowIdentifier, cancellationToken);
    }

    public static Dictionary<string, string> CreateInputs(
        WebAppFlowExecutionRequest request,
        string? headerTestRunId)
    {
        var inputs = request.Inputs is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(request.Inputs, StringComparer.OrdinalIgnoreCase);
        var testRunId = ResolveTestRunId(request.TestRunId, headerTestRunId);
        if (!string.IsNullOrWhiteSpace(testRunId))
        {
            inputs[AppExecutionContextKeys.TestRunIdInput] = testRunId;
        }

        return inputs;
    }

    public static List<string> ApplyInputVariables(Flow flow, Dictionary<string, string> inputs)
    {
        var missing = new List<string>();
        foreach (var input in inputs)
        {
            var matchingVariable = flow.InputVariables.Values
                .FirstOrDefault(variable => variable.Name.Equals(input.Key, StringComparison.OrdinalIgnoreCase));
            if (matchingVariable is null)
            {
                missing.Add(input.Key);
                continue;
            }

            matchingVariable.Value = input.Value;
        }

        return missing;
    }

    public static List<Guid> SelectRunners(
        IEnumerable<RunnerDetails> onlineRunners,
        List<string>? requestedNames,
        Guid? executionRunnerId)
    {
        if (requestedNames is { Count: > 0 })
        {
            return SelectNamedRunners(onlineRunners, requestedNames);
        }

        if (executionRunnerId.HasValue)
        {
            return SelectConfiguredRunner(onlineRunners, executionRunnerId.Value);
        }

        throw new InvalidOperationException("No runner specified. Provide RunnerNames or set an execution runner on the app.");
    }

    private static List<Guid> SelectNamedRunners(IEnumerable<RunnerDetails> onlineRunners, List<string> requestedNames)
    {
        var requestedSet = new HashSet<string>(requestedNames, StringComparer.OrdinalIgnoreCase);
        var matched = onlineRunners
            .Where(runner => requestedSet.Contains(runner.Name) && runner.Status == RunnerStatus.Online)
            .Select(runner => runner.Id)
            .ToList();
        if (matched.Count == 0)
        {
            throw new InvalidOperationException("None of the specified runners are online");
        }

        return matched;
    }

    private static List<Guid> SelectConfiguredRunner(IEnumerable<RunnerDetails> onlineRunners, Guid executionRunnerId)
    {
        var runner = onlineRunners.FirstOrDefault(candidate => candidate.Id == executionRunnerId);
        if (runner is null || runner.Status != RunnerStatus.Online)
        {
            throw new InvalidOperationException("Configured execution runner is not online");
        }

        return [executionRunnerId];
    }

    private static string? ResolveTestRunId(string? bodyTestRunId, string? headerTestRunId)
    {
        if (!string.IsNullOrWhiteSpace(bodyTestRunId))
        {
            return bodyTestRunId.Trim();
        }

        return string.IsNullOrWhiteSpace(headerTestRunId) ? null : headerTestRunId.Trim();
    }

    public sealed record FlowResolution(Guid? FlowId, Flow? Flow, string? NotFoundMessage);
    public sealed record RunnerSelection(List<Guid> RunnerIds, bool HasOnlineRunners, string? ErrorMessage);
    public sealed record FlowRunOutcome(
        List<string> MissingInputs,
        bool HasOnlineRunners,
        string? RunnerSelectionError,
        WebAppFlowExecutionResponse? Response);
    public sealed record FlowExecutionAttempt(
        Guid? FlowId,
        string? NotFoundMessage,
        List<string> MissingInputs,
        bool HasOnlineRunners,
        string? RunnerSelectionError,
        WebAppFlowExecutionResponse? Response);
}
