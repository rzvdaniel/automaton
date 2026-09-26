using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>
/// The stable XFS project identity owned by one user account. Hosted Runners
/// temporarily mirror this value for compatibility with existing workers.
/// </summary>
public class AccountStorageAllocation
{
    [Key]
    public Guid AccountId { get; set; }

    public int StorageProjectId { get; set; }

    /// <summary>
    /// Runner Host that owns the account's current XFS storage domain. Kept as
    /// an identity value rather than a foreign key so locality survives host
    /// retirement and can block an unsafe implicit move.
    /// </summary>
    public Guid? StorageRunnerHostId { get; set; }

    /// <summary>Verified XFS filesystem UUID for the account's storage domain.</summary>
    [MaxLength(80)]
    public string? StorageFilesystemUuid { get; set; }

    public virtual ApplicationUser Account { get; set; } = null!;
}
