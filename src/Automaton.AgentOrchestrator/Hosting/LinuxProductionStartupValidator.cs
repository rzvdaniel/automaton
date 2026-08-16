using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Hosting;

public static class LinuxProductionStartupValidator
{
    public static void Validate(WorkflowDefinition workflow, string applicationRoot)
    {
        var options = workflow.Options;
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("Production mode requires Linux.");
        if (!string.Equals(options.Tracker.Kind, "github-issues", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production tracker kind must be github-issues.");
        Require(options.Tracker.Provider.GetValueOrDefault("token"), "tracker credential");
        if (options.Agent.GitHub.Enabled) Require(options.Agent.GitHub.Token, "agent GitHub credential");
        if (options.Agent.MaxConcurrentAgents is < 1 or > 2) throw new InvalidOperationException("Production concurrency must be between 1 and 2 for Stage 13.");

        var workspace = Path.GetFullPath(options.Workspace.Root);
        var repository = Path.GetFullPath(options.Repository.Root);
        var release = Path.GetFullPath(applicationRoot);
        var config = Path.GetFullPath(workflow.SourcePath);
        RequireDisjoint(workspace, repository, "workspace root and canonical repository checkout must be disjoint");
        RequireDisjoint(workspace, release, "workspace root and immutable release directory must be disjoint");
        if (IsWithin(config, workspace)) throw new InvalidOperationException("Trusted workflow configuration must not be inside the workspace root.");
        if (options.Repository.Identity.Split('/').Length != 2) throw new InvalidOperationException("Production repository identity must be owner/repo.");

        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHome) || !Directory.Exists(codexHome) || !File.Exists(Path.Combine(codexHome, "auth.json")))
            throw new InvalidOperationException("Codex service-account authentication is unavailable in CODEX_HOME.");
        var executable = ProcessCommand(options.Codex.Command);
        if (!CommandExists(executable)) throw new InvalidOperationException("Configured Codex executable is unavailable.");
    }

    private static void Require(string? value, string category)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Required {category} is missing.");
    }

    private static void RequireDisjoint(string left, string right, string message)
    {
        if (IsWithin(left, right) || IsWithin(right, left)) throw new InvalidOperationException(message + ".");
    }

    private static bool IsWithin(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string ProcessCommand(string command)
    {
        var value = command.Trim();
        if (value.Length == 0) return value;
        if (value[0] == '"') { var closing = value.IndexOf('"', 1); return closing > 1 ? value[1..closing] : value; }
        var whitespace = value.IndexOfAny([' ', '\t']);
        return whitespace < 0 ? value : value[..whitespace];
    }

    private static bool CommandExists(string command)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command);
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, command)));
    }
}
