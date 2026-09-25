using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Api.Services;

/// <summary>
/// Dispatches workflows to authorized connected Runners and maps their execution responses.
/// </summary>
public sealed class RunnerFlowExecutionService(
    RunnerQueryService runnerQueryService,
    IHubContext<AutomatonHub> automatonHub)
{
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<RunnerFlowExecutionService>();

    public Task<IEnumerable<RunnerFlowResult>> ExecuteAsync(
        Guid flowId,
        IEnumerable<Guid> runnerIds,
        CancellationToken cancellationToken)
        => ExecuteAsync(flowId, runnerIds, null, cancellationToken);

    public Task<IEnumerable<RunnerFlowResult>> ExecuteAsync(
        Guid flowId,
        IEnumerable<Guid> runnerIds,
        Guid? userId,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(flowId, runnerIds, userId, cancellationToken);

    private async Task<IEnumerable<RunnerFlowResult>> ExecuteCoreAsync(
        Guid flowId,
        IEnumerable<Guid> runnerIds,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        _ = userId;
        var results = new List<RunnerFlowResult>();
        foreach (var runnerId in runnerIds)
        {
            var runner = runnerQueryService.Get(runnerId);
            var client = automatonHub.Clients.Client(runner.ConnectionId);
            try
            {
                var response = await client.InvokeAsync<WorkflowExecution>(
                    AutomatonHubMethods.RunWorkflow, flowId, cancellationToken);
                results.Add(GetSuccessfulFlowResult(flowId, runnerId, response));
            }
            catch (Exception ex)
            {
                logger.ForContext("FlowId", flowId)
                    .ForContext("RunnerId", runnerId)
                    .Error(ex, "An error happened when executing flow on runner");
                results.Add(GetInvalidFlowResult(flowId, runnerId));
            }
        }

        return results;
    }

    private static RunnerFlowResult GetSuccessfulFlowResult(Guid flowId, Guid runnerId, WorkflowExecution flowExecution)
        => new()
        {
            FlowId = flowId,
            RunnerId = runnerId,
            Started = flowExecution.Started,
            Finished = flowExecution.Finished,
            Status = flowExecution.Status,
            OutputVariables = flowExecution.OutputVariables
                .ToDictionary(variable => variable.Key, variable => variable.Value.Value),
            Logs = flowExecution.Logs
                .Select(log => $"[{log.Timestamp:HH:mm:ss}] [{log.Level}] {log.Message}")
                .ToList()
        };

    private static RunnerFlowResult GetInvalidFlowResult(Guid flowId, Guid runnerId)
        => new()
        {
            FlowId = flowId,
            RunnerId = runnerId,
            Started = DateTime.MinValue,
            Finished = DateTime.MinValue,
            Status = WorkflowStatus.None
        };
}
