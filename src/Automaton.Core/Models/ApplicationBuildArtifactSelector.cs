namespace Automaton.Core.Models;

/// <summary>
/// Defines which post-build workspace directory becomes the runtime artifact.
/// This rule is shared by package creation and Runner deployment.
/// </summary>
public static class ApplicationBuildArtifactSelector
{
    public static string GetRelativeDirectory(ApplicationContract application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return !string.IsNullOrWhiteSpace(application.Build.OutputDirectory)
            ? application.Build.OutputDirectory!
            : application.Runtime.Command?.WorkingDirectory ?? ".";
    }
}
