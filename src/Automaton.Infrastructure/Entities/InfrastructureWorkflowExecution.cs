using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public class InfrastructureWorkflowExecution
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(80)]
    public string WorkflowVersionId { get; set; } = string.Empty;

    public Guid RequestedByUserId { get; set; }
    public Guid TargetRunnerId { get; set; }
    public Guid TargetRunnerHostId { get; set; }

    [Required, MaxLength(32)]
    public string Status { get; set; } = InfrastructureWorkflowExecutionStatuses.Queued;

    [Required]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? ExitCode { get; set; }

    [MaxLength(2000)]
    public string? Error { get; set; }

    [Required, MaxLength(160)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? ResultJson { get; set; }

    public virtual ICollection<InfrastructureWorkflowExecutionLog> Logs { get; set; } = new List<InfrastructureWorkflowExecutionLog>();
}

public static class InfrastructureWorkflowExecutionStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
