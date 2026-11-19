namespace Automaton.Api.Entities;

/// <summary>An app one person pinned to their Studio Home. Pins are private to that person.</summary>
public class UserHomePin
{
    public Guid UserId { get; set; }
    public Guid AppId { get; set; }
    public DateTime PinnedAtUtc { get; set; }
    /// <summary>Lower comes first; new pins go last.</summary>
    public int SortOrder { get; set; }
    /// <summary>True when the card spans two columns on Home.</summary>
    public bool Wide { get; set; }
}
