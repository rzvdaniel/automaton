using Automaton.Core.Models;

namespace Automaton.Api.Services;

public sealed class RunnerExecutionDispatchService(
    RunnerExecutionAdmissionService admission, RunnerExecutionOperationService operations)
{
    public async Task<RunnerExecutionResult> ExecuteAsync(Guid runnerId, Guid? userId,
        RunnerExecutionRequest request, CancellationToken token)
    {
        var actor = await admission.AuthorizeAsync(runnerId, userId, token);
        var id = await operations.QueueAsync(runnerId, actor, request, token);
        return await operations.WaitAsync(id, token);
    }
}
