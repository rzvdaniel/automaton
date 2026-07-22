namespace Automaton.Api.Entities;

public class AppModuleExecution
{
    public Guid Id { get; set; }
    public Guid? HostedRunnerOperationId { get; set; }
    public HostedRunnerDeployment? HostedRunnerOperation { get; set; }
    public Guid AppId { get; set; }
    public string ModuleType { get; set; } = string.Empty;
    public string ModuleKey { get; set; } = string.Empty;
    public Guid? RunnerId { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public Guid? RequestedByUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DeploymentHash { get; set; }
    public int? ExitCode { get; set; }
    public string? ErrorCode { get; set; }
    public string? FailureSummary { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? ExecutionDeadlineUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? LogsAvailableUntilUtc { get; set; }
    public DateTime? ResultAvailableUntilUtc { get; set; }
    public string? CorrelationId { get; set; }
}
