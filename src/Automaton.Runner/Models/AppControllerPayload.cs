using Automaton.Core.Models;

namespace Automaton.Runner.Console.Models;

/// <summary>
/// Payload for app controller action execution sent from server to runner.
/// Code is NOT included — runner reads the script from its local persistent volume (deployed separately).
/// </summary>
public class AppControllerPayload
{
    public Guid AppId { get; set; }
    public string ControllerName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public Dictionary<string, string>? Inputs { get; set; }
    public List<string>? OutputVariableNames { get; set; }
}
