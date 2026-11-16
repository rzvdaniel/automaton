namespace Automaton.Api.Entities;

/// <summary>
/// An address the owner of a public app added for it, such as blog.example.com. It routes only after its DNS
/// points at this server (<see cref="VerifiedAt"/>). While a primary domain exists, every other address redirects to it.
/// </summary>
public class AppCustomDomain
{
    public Guid Id { get; set; }
    public Guid WebAppId { get; set; }

    /// <summary>Lower-case host name; unique across the server.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Secret the owner publishes in a DNS TXT record to prove they control the domain.</summary>
    public string VerificationToken { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>Set while the server's own check finds the domain serving the app over a valid certificate; only a live domain can be primary.</summary>
    public DateTimeOffset? LiveAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
