using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public class HostedRunnerStorageObservation
{
    [Key] public Guid Id { get; set; }
    public Guid HostedRunnerId { get; set; }
    public virtual HostedRunner HostedRunner { get; set; } = null!;
    [Required, MaxLength(40)] public string Source { get; set; } = string.Empty;
    public DateTime MeasuredAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public long SystemBytes { get; set; }
    public long UserBytes { get; set; }
    public long? TotalBytes { get; set; }
    public bool Success { get; set; }
    [MaxLength(2000)] public string? Error { get; set; }
    [MaxLength(80)] public string? RunnerVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
