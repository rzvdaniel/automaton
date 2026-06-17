namespace Automaton.Api.Config;

public class AppFileRuleBaselineOptions
{
    public List<string> SourceExclude { get; set; } = [];
    public List<string> FrontendExclude { get; set; } = [];
    public List<string> BackendExclude { get; set; } = [];
    public List<string> RemixSourceExclude { get; set; } = [];
    public List<string> RemixFrontendExclude { get; set; } = [];
}
