using Automaton.Api.Models;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public class HostedRunnerStorageVerification
{
    [Key] public Guid Id { get; set; }
    public Guid HostedRunnerId { get; set; }
    public virtual HostedRunner HostedRunner { get; set; } = null!;
    [Required, MaxLength(160)] public string IdempotencyKey { get; set; } = string.Empty;
    [Required, MaxLength(32)] public string Status { get; set; } = StorageVerificationStatuses.Pending;
    [Required, MaxLength(80)] public string Reason { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    [MaxLength(2000)] public string? Error { get; set; }
}
