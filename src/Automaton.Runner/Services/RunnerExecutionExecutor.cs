using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerExecutionExecutor(IServiceProvider services) : IRunnerExecutionExecutor
{
    public async Task<RunnerExecutionResult> ExecuteAsync(RunnerExecutionRequest request, CancellationToken token)
    {
        using var scope = services.CreateScope();
        if (request.FlowId.HasValue && request.Step is null)
        {
            var execution = await scope.ServiceProvider.GetRequiredService<FlowExecutionService>()
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
        var result = await scope.ServiceProvider.GetRequiredService<CustomStepExecuteService>().RunCustomStep(new CustomStepPayload
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
