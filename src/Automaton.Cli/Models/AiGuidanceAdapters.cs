namespace Automaton.Cli.Models;

public static class AiGuidanceAdapters
{
    public const string None = "none";
    public const string Claude = "claude";
    public const string Copilot = "copilot";
    public const string GitHubSkills = "github-skills";
    public const string All = "all";

    public static bool IsKnown(string adapter)
    {
        return string.Equals(adapter, None, StringComparison.OrdinalIgnoreCase)
            || string.Equals(adapter, Claude, StringComparison.OrdinalIgnoreCase)
            || string.Equals(adapter, Copilot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(adapter, GitHubSkills, StringComparison.OrdinalIgnoreCase)
            || string.Equals(adapter, All, StringComparison.OrdinalIgnoreCase);
    }
}
