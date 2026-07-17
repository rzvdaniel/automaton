using Automaton.Api.Models;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public class HostedRunnerSpecification
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(80)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public int Revision { get; set; } = 1;
    public int SchemaVersion { get; set; } = 1;
    public HostedRunnerResources Resources { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public virtual ICollection<HostedRunnerSpecificationRevision> Revisions { get; set; } = new List<HostedRunnerSpecificationRevision>();
}

public class HostedRunnerSpecificationRevision
{
    [Key]
    public Guid Id { get; set; }
    public Guid SpecificationId { get; set; }
    public virtual HostedRunnerSpecification Specification { get; set; } = null!;
    public int Revision { get; set; }
    public int SchemaVersion { get; set; }
    public HostedRunnerResources Resources { get; set; } = new();
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(2000)]
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

public class HostedRunnerPlatformSettings
{
    [Key]
    public Guid Id { get; set; }
    public Guid DefaultSpecificationId { get; set; }
    public virtual HostedRunnerSpecification DefaultSpecification { get; set; } = null!;
    public bool ProvisioningEnabled { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
}
