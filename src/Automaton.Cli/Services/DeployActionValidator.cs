using Automaton.Cli.Models;
using Automaton.Core.Models;

namespace Automaton.Cli.Services;

/// <summary>Validates that every discovered Action manifest has a safe, existing entrypoint.</summary>
internal static class DeployActionValidator
{
    internal static DeployCheckResult Validate(
        string directory, ISet<string> nextSteps, out int actionCount)
    {
        try
        {
            var actions = AppActionManifestParser.Discover(directory);
            ValidateEntrypoints(actions);
            actionCount = actions.Count;
            var message = actionCount == 0
                ? "no Actions found"
                : $"validated {actionCount} Action module(s)";
            return new DeployCheckResult("actions", true, message);
        }
        catch (Exception exception)
        {
            actionCount = 0;
            nextSteps.Add("Fix Action manifests under actions/{action-key}/action.yaml and rerun automaton deploy");
            return new DeployCheckResult("actions", false, exception.Message);
        }
    }

    private static void ValidateEntrypoints(IEnumerable<DiscoveredAppAction> actions)
    {
        foreach (var action in actions)
        {
            var actionRoot = Path.GetFullPath(action.DirectoryPath);
            var entrypoint = Path.GetFullPath(Path.Combine(
                action.DirectoryPath, action.Manifest.Spec.Entrypoint));
            var isWithinAction = entrypoint.StartsWith(
                actionRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!isWithinAction || !File.Exists(entrypoint))
            {
                throw new InvalidOperationException(
                    $"Action '{action.Key}' entrypoint '{action.Manifest.Spec.Entrypoint}' does not exist.");
            }
        }
    }
}
