using Automaton.Core.Enums;

namespace Automaton.Core.Models;

public sealed record RunnerExecutionCommand(Guid OperationId, Guid RunnerId);
public sealed record RunnerExecutionRequest(Guid? FlowId, RunnerCustomStepPayload? Step, AppActionCommand? Action = null);
public sealed record RunnerExecutionAssignment(string Disposition, RunnerExecutionRequest? Request);
public sealed record RunnerExecutionReceipt(string Outcome);

public sealed class RunnerCustomStepPayload
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public List<StepVariable>? CodeInputVariables { get; set; }
    public List<StepVariable>? CodeOutputVariables { get; set; }
}

public sealed class RunnerExecutionResult
{
    public string Outcome { get; set; } = "Failed";
    public string? Error { get; set; }
    public WorkflowStatus WorkflowStatus { get; set; } = WorkflowStatus.Error;
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public string? Output { get; set; }
    public System.Text.Json.JsonElement? ActionResult { get; set; }
    public Dictionary<string, string?> OutputVariables { get; set; } = [];
    public List<string> Logs { get; set; } = [];
    public static RunnerExecutionResult Interrupted(string reason) => new()
    {
        Outcome = "Lost", Error = reason, Logs = [$"[Error] {reason}"], Started = DateTime.UtcNow, Finished = DateTime.UtcNow
    };
}
