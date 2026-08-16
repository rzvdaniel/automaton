using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Integration.GitHub;

/// <summary>
/// Builds the narrow GitHub capability explicitly granted to a supervised Codex child.
/// Host credentials are never inherited implicitly.
/// </summary>
public static class GitHubAgentCapability
{
    public const string TokenEnvironmentVariable = "GH_TOKEN";

    public const string Instruction = """
GitHub repository-workflow capability is enabled for this task. Use the assigned branch only: commit the completed change, push that branch, and create or update one draft pull request targeting the configured base branch. Inspect CI and actionable review feedback for that draft pull request, and address it when the assigned task requires it. Never merge or approve a pull request, force-push, change the remote or base branch, or reveal credentials.
""";

    public static IReadOnlyDictionary<string, string> BuildChildEnvironment(AgentGitHubOptions options)
    {
        if (!options.Enabled) return new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(options.Token))
            throw new InvalidOperationException("The enabled GitHub agent capability has no credential.");

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TokenEnvironmentVariable] = options.Token
        };
    }
}
