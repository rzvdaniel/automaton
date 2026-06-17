namespace Automaton.Api.Models;

public class AppDeployRulesSettings
{
    public List<string> SourceExclude { get; set; } = [];
    public List<string> FrontendExclude { get; set; } = [];
    public List<string> BackendExclude { get; set; } = [];
    public List<string> RemixSourceExclude { get; set; } = [];
    public List<string> RemixFrontendExclude { get; set; } = [];
}
