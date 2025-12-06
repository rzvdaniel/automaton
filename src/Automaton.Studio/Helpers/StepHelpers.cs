namespace Automaton.Studio.Helpers;

/// <summary>
/// Shared helper methods for Custom Step operations
/// </summary>
public static class StepHelpers
{
    /// <summary>
    /// Gets the AntDesign color tag for a given step language
    /// </summary>
    public static string GetLanguageColor(string? language)
    {
        return language?.ToLower() switch
        {
            "python" => "blue",
            "batch" => "cyan",
            "powershell" => "purple",
            _ => "default"
        };
    }

    /// <summary>
    /// Gets an appropriate icon type for a given step language
    /// </summary>
    public static string GetStepIcon(string? language)
    {
        return language?.ToLower() switch
        {
            "python" => "code",
            "batch" => "code",
            "powershell" => "code",
            _ => "api"
        };
    }
}
