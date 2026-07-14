using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public class InfrastructureWorkflowExecutionLog
{
    [Key]
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public int Sequence { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(16)]
    public string Stream { get; set; } = "stdout";

    [Required]
    public string Message { get; set; } = string.Empty;

    public virtual InfrastructureWorkflowExecution Execution { get; set; } = null!;
}
