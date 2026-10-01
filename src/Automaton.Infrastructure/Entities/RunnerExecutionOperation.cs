using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>One durable workflow or custom-step invocation on a stable Runner identity.</summary>
[Microsoft.EntityFrameworkCore.Index(nameof(RunnerId), nameof(CompletedAtUtc))]
public sealed class RunnerExecutionOperation
{
    public Guid Id { get; set; }
    public Guid RunnerId { get; set; }
    public Guid RequestedByUserId { get; set; }
    [Required] public string ProtectedRequest { get; set; } = string.Empty;
    public string? ResultJson { get; set; }
    public bool CancellationRequested { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
