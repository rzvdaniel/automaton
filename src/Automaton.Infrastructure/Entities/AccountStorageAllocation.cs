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

    public virtual ApplicationUser Account { get; set; } = null!;
}
