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

    /// <summary>When true, this plan is automatically assigned to new users.</summary>
    public bool IsDefault { get; set; } = false;

    // ── Feature gates ────────────────────────────────────────────────────────
    // These flags are checked at the API level to allow/deny feature access.
    // Enforcement is done in service/controller layer — not yet wired up.

    /// <summary>User can create and manage Projects (multi-asset containers with shared settings).</summary>
    public bool CanCreateProjects { get; set; } = false;

    /// <summary>User can create and manage Organizations (multi-user teams).</summary>
    public bool CanCreateOrganizations { get; set; } = false;

    /// <summary>User can register and use custom runner nodes.</summary>
    public bool CanUseCustomRunners { get; set; } = false;

    /// <summary>Maximum number of flows the user may own. 0 = unlimited.</summary>
    public int MaxFlows { get; set; } = 5;

    /// <summary>Maximum number of apps the user may own. 0 = unlimited.</summary>
    public int MaxApps { get; set; } = 2;

    /// <summary>Maximum number of custom steps the user may own. 0 = unlimited.</summary>
    public int MaxCustomSteps { get; set; } = 3;

    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
