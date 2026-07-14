namespace Automaton.Api.Models;

public sealed record StartHostValidationRequest
{
    public Guid RunnerHostId { get; init; }
    public Guid? RunnerId { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed record InfrastructureWorkflowExecutionResponse
{
    public Guid Id { get; init; }
    public string WorkflowVersionId { get; init; } = string.Empty;
    public Guid RequestedByUserId { get; init; }
    public Guid TargetRunnerId { get; init; }
    public Guid TargetRunnerHostId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public int? ExitCode { get; init; }
    public string? Error { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public string? ResultJson { get; init; }
}

public sealed record InfrastructureWorkflowExecutionLogResponse
{
    public int Sequence { get; init; }
    public DateTime TimestampUtc { get; init; }
    public string Stream { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
