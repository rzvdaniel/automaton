using Automaton.Api.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Automaton.Api.Entities;

/// <summary>
/// A physical or virtual machine that can host paid user runner containers.
/// </summary>
public class RunnerHost
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Provider { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? Region { get; set; }

    [MaxLength(120)]
    public string? PublicIp { get; set; }

    public bool IsLocal { get; set; }

    [Required, MaxLength(40)]
    public string Status { get; set; } = RunnerHostStatuses.Active;

    [Column(TypeName = "decimal(5,2)")]
    public decimal TotalCpuCores { get; set; } = 2m;

    public int TotalMemoryMb { get; set; } = 4096;
    public int TotalDiskMb { get; set; } = 40960;

    /// <summary>
    /// Shared Runner installed with infrastructure privileges on this host.
    /// </summary>
    public Guid? InfrastructureRunnerId { get; set; }
    public virtual Runner? InfrastructureRunner { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<HostedRunner> HostedRunners { get; set; } = new List<HostedRunner>();
    public virtual ICollection<RunnerEnrollment> RunnerEnrollments { get; set; } = new List<RunnerEnrollment>();
}
