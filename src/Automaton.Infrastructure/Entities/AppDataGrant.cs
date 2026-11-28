namespace Automaton.Api.Entities;

/// <summary>
/// An owner-approved grant that lets one app (the grantee, such as an admin tool) mount a data volume of another app
/// (the source). Read-only unless <see cref="ReadWrite"/> was confirmed.
/// </summary>
public class AppDataGrant
{
    public Guid Id { get; set; }
    public Guid SourceAppId { get; set; }
    public Guid GranteeAppId { get; set; }

    /// <summary>A volume declared in the source app's compose contract.</summary>
    public string Volume { get; set; } = string.Empty;

    public bool ReadWrite { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
