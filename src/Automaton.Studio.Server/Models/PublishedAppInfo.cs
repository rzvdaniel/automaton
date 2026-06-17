namespace Automaton.Api.Models;

/// <summary>
/// Lightweight model for published app metadata used for access validation.
/// Does not include the full app definition to avoid exposing sensitive data.
/// </summary>
public class PublishedAppInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string PublicUrl { get; set; }
    public bool RequiresAuthentication { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? ExecutionRunnerId { get; set; }
}
