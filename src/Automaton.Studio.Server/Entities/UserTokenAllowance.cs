using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Tracks per-user monthly AI token consumption and allowance settings.
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

    /// <summary>Monthly token budget. Default: 1,000,000 (effectively unlimited for dev).</summary>
    public int MonthlyTokenLimit { get; set; } = 1_000_000;

    /// <summary>Running counter of tokens used in the current billing month.</summary>
    public int CurrentMonthTokens { get; set; } = 0;

    /// <summary>The month (1–12) to which CurrentMonthTokens belongs. Resets counter when changed.</summary>
    public int CurrentMonth { get; set; }

    /// <summary>The year to which CurrentMonthTokens belongs. Resets counter when changed.</summary>
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
