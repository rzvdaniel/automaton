namespace Automaton.Studio.Server.Models;

public class AppDeployRulesSettings
{
    public List<string> SourceExclude { get; set; } = [];
    public List<string> FrontendExclude { get; set; } = [];
    public List<string> BackendExclude { get; set; } = [];
}
