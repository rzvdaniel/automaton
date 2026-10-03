using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerExecutionExecutor(IServiceProvider services) : IRunnerExecutionExecutor, IDisposable
{
    private readonly SemaphoreSlim workflowGate = new(1, 1);

    public void Dispose() => workflowGate.Dispose();

    public async Task<RunnerExecutionResult> ExecuteAsync(RunnerExecutionRequest request, CancellationToken token)
    {
        using var scope = services.CreateScope();
        EnsureSinglePayload(request);
        if (request.ManagedArtifacts is not null)
            return new ManagedApplicationArtifactExecutor(scope.ServiceProvider.GetRequiredService<RunnerWorkspaceService>())
                .Execute(request.ManagedArtifacts, token);
        if (request.FileTransfer is not null)
            throw new InvalidOperationException("File transfers require an authenticated Runner execution client.");
        if (request.Action is not null)
        {
            return await new AppActionCommandExecutor(
                scope.ServiceProvider.GetRequiredService<AppActionExecutionService>(),
                scope.ServiceProvider.GetRequiredService<AppActionDeploymentService>(),
                scope.ServiceProvider.GetRequiredService<RunnerDeploymentService>()).ExecuteAsync(request.Action, token);
        }
        await workflowGate.WaitAsync(token);
        try { return await ExecuteWorkflowOrStepAsync(scope.ServiceProvider, request, token); }
        finally { workflowGate.Release(); }
    }

    private static void EnsureSinglePayload(RunnerExecutionRequest request)
    {
        var count = (request.FlowId.HasValue ? 1 : 0) + (request.Step is not null ? 1 : 0)
            + (request.Action is not null ? 1 : 0) + (request.ManagedArtifacts is not null ? 1 : 0)
            + (request.FileTransfer is not null ? 1 : 0);
        if (count != 1) throw new InvalidDataException("Exactly one Runner execution payload is required.");
    }

    private static async Task<RunnerExecutionResult> ExecuteWorkflowOrStepAsync(IServiceProvider provider,
        RunnerExecutionRequest request, CancellationToken token)
    {
        if (request.FlowId.HasValue && request.Step is null)
        {
            var execution = await provider.GetRequiredService<FlowExecutionService>()
                .ExecuteWorkflowAsync(request.FlowId.Value, token);
            return new()
            {
                Outcome = execution.Status == WorkflowStatus.Success ? "Succeeded" : "Failed",
                WorkflowStatus = execution.Status, Started = execution.Started, Finished = execution.Finished,
                OutputVariables = execution.OutputVariables.ToDictionary(v => v.Key, v => v.Value.Value),
                Logs = execution.Logs.Select(l => $"[{l.Timestamp:HH:mm:ss}] [{l.Level}] {l.Message}").ToList()
            };
        }
        if (request.FlowId.HasValue || request.Step is null)
            throw new InvalidDataException("Exactly one workflow or custom step is required.");
        token.ThrowIfCancellationRequested();
        var step = request.Step;
        var result = await provider.GetRequiredService<CustomStepExecuteService>().RunCustomStep(new CustomStepPayload
        {
            Id = step.Id, Name = step.Name, Code = step.Code, Language = step.Language,
            CodeInputVariables = step.CodeInputVariables ?? [], CodeOutputVariables = step.CodeOutputVariables ?? []
        });
        return new()
        {
            Outcome = result.Success ? "Succeeded" : "Failed", Output = result.Output, Error = result.Error,
            Started = result.Started, Finished = result.Finished,
            OutputVariables = result.OutputVariables?.ToDictionary(v => v.Key, v => (string?)v.Value) ?? []
        };
    }
}
