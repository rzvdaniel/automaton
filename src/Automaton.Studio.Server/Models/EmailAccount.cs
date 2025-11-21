using System.Text.Json.Serialization;

namespace Automaton.Studio.Server.Models;

public class EmailAccount
{
    [JsonInclude]
    public required string Email { get; set; }
    [JsonInclude]
    public string? Name { get; set; }
}
