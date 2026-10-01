using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Api.Services;

/// <summary>
/// Dispatches workflows to authorized Runners through durable operations and maps their execution responses.
/// </summary>
public sealed class RunnerFlowExecutionService(
    RunnerExecutionDispatchService dispatch)
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
        var results = new List<RunnerFlowResult>();
        foreach (var runnerId in runnerIds)
        {
            try
            {
                var response = await dispatch.ExecuteAsync(runnerId, userId, new(flowId, null), cancellationToken);
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

    private static RunnerFlowResult GetSuccessfulFlowResult(Guid flowId, Guid runnerId, RunnerExecutionResult flowExecution)
        => new()
        {
            FlowId = flowId,
            RunnerId = runnerId,
            Started = flowExecution.Started,
            Finished = flowExecution.Finished,
            Status = flowExecution.WorkflowStatus,
            OutputVariables = flowExecution.OutputVariables,
            Logs = flowExecution.Logs
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
