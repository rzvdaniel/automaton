using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Automaton.Api.Entities;

/// <summary>
/// Defines a subscription product. Feature details live in CapabilitiesJson so new
/// subscription capabilities can be added without growing this table forever.
/// </summary>
public class SubscriptionPlan
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Subscription price charged to the user per month (USD).</summary>
    [Column(TypeName = "decimal(10, 2)")]
    public decimal PriceUsd { get; set; } = 0.00m;

    /// <summary>Stripe Product ID (e.g. prod_xxx). Set when Stripe is configured.</summary>
    [MaxLength(100)]
    public string? StripeProductId { get; set; }

    /// <summary>Stripe Price ID for monthly recurring billing (e.g. price_xxx).</summary>
    [MaxLength(100)]
    public string? StripePriceId { get; set; }

    /// <summary>When true, this plan is automatically assigned to new users.</summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>When false, the plan stays in admin history but is hidden from user-facing pricing and checkout.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>JSON-serialized Automaton.Api.Models.PlanCapabilities value.</summary>
    public string CapabilitiesJson { get; set; } = "{}";

    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
