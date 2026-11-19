namespace Automaton.Api.Entities;

/// <summary>One person's Home preferences. A person without a row has the defaults (cards).</summary>
public class UserHomeSetting
{
    public Guid UserId { get; set; }
    /// <summary>True when Home shows one compact row per pinned app instead of cards.</summary>
    public bool ListView { get; set; }
}
