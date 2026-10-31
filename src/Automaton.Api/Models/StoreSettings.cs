namespace Automaton.Api.Models;

/// <summary>
/// Installation-wide visibility of the public app store at store.&lt;domain&gt;.
/// </summary>
public class StoreSettings
{
    public const string Public = "public";
    public const string Disabled = "disabled";

    public string Mode { get; set; } = Public;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEnabled => Mode == Public;
}

public sealed class PublicStoreStatusDto
{
    public bool Enabled { get; init; }
}
