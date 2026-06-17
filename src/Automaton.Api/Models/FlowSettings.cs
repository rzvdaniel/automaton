#nullable disable

using Automaton;

namespace Automaton.Api.Models;

public class FlowSettings
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
}
