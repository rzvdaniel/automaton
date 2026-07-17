using Automaton.Api.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Automaton.Api.Entities;

/// <summary>
/// A managed runner container allocated to one paid user.
/// </summary>
public class HostedRunner
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public virtual ApplicationUser User { get; set; } = null!;

    public Guid? RunnerHostId { get; set; }
    public virtual RunnerHost? RunnerHost { get; set; }

    public Guid? RunnerId { get; set; }
    public virtual Runner? Runner { get; set; }

    public Guid? ApiKeyId { get; set; }
    public virtual ApiKey? ApiKey { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Status { get; set; } = HostedRunnerStatuses.Pending;

    public Guid HostedRunnerSpecificationId { get; set; }
    public virtual HostedRunnerSpecification HostedRunnerSpecification { get; set; } = null!;
    public int SpecificationRevision { get; set; }
    public HostedRunnerSpecificationSnapshot SpecificationSnapshot { get; set; } = new();
    public HostedRunnerSpecificationSnapshot? PendingSpecificationSnapshot { get; set; }

    [Required, MaxLength(160)]
    public string ContainerName { get; set; } = string.Empty;

    [MaxLength(160)]
    public string? ContainerId { get; set; }

    [MaxLength(2000)]
    public string? RuntimeEndpoint { get; set; }

    /// <summary>
    /// Host port published for the Runner gateway when this Runner is remote.
    /// Same-host Docker deployments use the container network and leave this null.
    /// </summary>
    public int? RuntimePort { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ProvisionedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<HostedRunnerDeployment> Deployments { get; set; } = new List<HostedRunnerDeployment>();
}
