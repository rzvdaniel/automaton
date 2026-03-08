using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Defines a subscription tier. Admins create/edit plans; users are assigned to plans.
/// The plan's MonthlyCostLimitUsd is copied to UserTokenAllowance when the plan is assigned.
/// </summary>
public class SubscriptionPlan
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Monthly AI spending budget for users on this plan (USD).</summary>
    [Column(TypeName = "decimal(10, 2)")]
    public decimal MonthlyCostLimitUsd { get; set; } = 10.00m;

    /// <summary>Subscription price charged to the user per month (USD). Independent of AI budget.</summary>
    [Column(TypeName = "decimal(10, 2)")]
    public decimal PriceUsd { get; set; } = 0.00m;

    /// <summary>When true, this plan is automatically assigned to new users.</summary>
    public bool IsDefault { get; set; } = false;

    // ── Feature gates ────────────────────────────────────────────────────────

    /// <summary>User can create and manage Projects (multi-asset containers with shared settings).</summary>
    public bool CanCreateProjects { get; set; } = false;

    /// <summary>User can create and manage Organizations (multi-user teams).</summary>
    public bool CanCreateOrganizations { get; set; } = false;

    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
