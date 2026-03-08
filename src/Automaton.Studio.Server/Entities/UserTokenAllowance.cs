using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Tracks per-user monthly AI token consumption and allowance settings.
/// Tracks input/output tokens separately and computes real USD cost for billing.
/// Uses a running counter with lazy month reset — no background jobs required.
/// Audit history is available via AiChatMessage.
/// </summary>
public class UserTokenAllowance
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Unique per user.</summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>Monthly cost budget in USD. Default: $10.00 (free tier).</summary>
    [Column(TypeName = "decimal(10, 6)")]
    public decimal MonthlyCostLimitUsd { get; set; } = 10.00m;

    /// <summary>Running input token count for the current billing month.</summary>
    public int CurrentMonthInputTokens { get; set; } = 0;

    /// <summary>Running output token count for the current billing month.</summary>
    public int CurrentMonthOutputTokens { get; set; } = 0;

    /// <summary>Running cost in USD for the current billing month.</summary>
    [Column(TypeName = "decimal(10, 6)")]
    public decimal CurrentMonthCostUsd { get; set; } = 0m;

    /// <summary>The month (1–12) to which the counters belong. Resets all counters when changed.</summary>
    public int CurrentMonth { get; set; }

    /// <summary>The year to which the counters belong. Resets all counters when changed.</summary>
    public int CurrentYear { get; set; }

    /// <summary>What to do when the user exceeds their monthly limit: "Downgrade" or "Block".</summary>
    [MaxLength(50)]
    public string OverageAction { get; set; } = OverageActions.Downgrade;

    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser User { get; set; } = null!;
}

public static class OverageActions
{
    public const string Downgrade = "Downgrade";
    public const string Block = "Block";
}
