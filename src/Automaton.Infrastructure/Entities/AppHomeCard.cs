namespace Automaton.Api.Entities;

/// <summary>The latest valid card an app's Action produced for Studio Home: one row per app.</summary>
public class AppHomeCard
{
    public Guid AppId { get; set; }
    /// <summary>The validated card as JSON: status, up to 3 facts, a few alerts. Plain text only.</summary>
    public string CardJson { get; set; } = "{}";
    /// <summary>When the run that produced the card finished.</summary>
    public DateTime UpdatedAtUtc { get; set; }
    public Guid ExecutionId { get; set; }
    /// <summary>The newest <c>home-card</c> run that failed or returned an invalid card; the card is greyed while it is newer.</summary>
    public DateTime? LastFailedAtUtc { get; set; }
    /// <summary>Why the newest card the app sent was not valid; Studio then shows the default card. Null when the newest card was valid.</summary>
    public string? InvalidReason { get; set; }
}
