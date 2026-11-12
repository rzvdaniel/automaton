using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerExecutionExecutor(IServiceProvider services) : IRunnerExecutionExecutor, IDisposable
{
    public void Dispose() { }

    public async Task<RunnerExecutionResult> ExecuteAsync(RunnerExecutionRequest request, CancellationToken token)
    {
        using var scope = services.CreateScope();
        EnsureSinglePayload(request);
        if (request.HostContainer is not null)
        {
            var started = DateTime.UtcNow;
            var result = await scope.ServiceProvider.GetRequiredService<HostContainerExecutionService>()
                .ExecuteAsync(request.HostContainer, token);
            var finished = DateTime.UtcNow;
            return new RunnerExecutionResult
            {
                Outcome = result.Success ? "Succeeded" : "Failed",
                Started = started,
                Finished = finished,
                Error = result.Error,
                HostContainerResult = result
            };
        }
        if (request.FileTransfer is not null)
            throw new InvalidOperationException("File transfers require an authenticated Runner execution client.");
        if (request.Action is not null)
        {
            return await scope.ServiceProvider.GetRequiredService<AppActionCommandExecutor>()
                .ExecuteAsync(request.Action, token);
        }
        throw new InvalidDataException("Exactly one Runner execution payload is required.");
    }

    private static void EnsureSinglePayload(RunnerExecutionRequest request)
    {
        var count = (request.Action is not null ? 1 : 0)
            + (request.FileTransfer is not null ? 1 : 0) + (request.HostContainer is not null ? 1 : 0);
        if (count != 1) throw new InvalidDataException("Exactly one Runner execution payload is required.");
    }
}
