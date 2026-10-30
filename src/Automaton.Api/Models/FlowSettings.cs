#nullable disable

using Automaton;
using System.Text.Json.Serialization;

namespace Automaton.Api.Models;

public class FlowSettings
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
}

public class UpdateFlowSettingsRequest
{
    [JsonRequired]
    public string Name { get; set; }
}
