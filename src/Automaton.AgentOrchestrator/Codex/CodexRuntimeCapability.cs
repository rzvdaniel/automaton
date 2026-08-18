using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Codex;

/// <summary>
/// Defines the narrowly scoped repository-workflow capability for a Codex child.
/// The current implementation maps the explicit GitHub workflow setting to the
/// standard gh credential, without exposing provider details to the supervisor.
/// </summary>
public sealed record CodexRuntimeCapability(
    bool RepositoryWorkflowEnabled,
    IReadOnlyDictionary<string, string> ChildEnvironment,
    string GitHubAccess = "none")
{
    private const string RepositoryWorkflowInstruction = """
Repository workflow capability is enabled for this task. Use the assigned branch only: commit the completed change, push that branch, and create or update one draft pull request targeting the configured base branch. Inspect checks and actionable review feedback for that draft pull request, and address it when the assigned task requires it. Never merge or approve a pull request, force-push, change the remote or base branch, or reveal credentials.
""";

    private const string NoRepositoryWorkflowInstruction = "Repository workflow capability is not enabled; do not attempt to publish or merge changes.";

    public string Instruction => GitHubAccess switch
    {
        "write" => RepositoryWorkflowInstruction,
        "read-only" => "GitHub read-only capability is enabled. You may inspect issues, pull requests, checks, and feedback, but must not commit, push, create or update pull requests, merge, approve, or change remote state.",
        _ => NoRepositoryWorkflowInstruction
    };

    public static CodexRuntimeCapability From(AgentExecutionOptions options, AgentExecutionProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var access = profile?.GitHubAccess ?? (options.GitHub.Enabled ? "write" : "none");
        if (!options.GitHub.Enabled || access == "none") return new(false, new Dictionary<string, string>(), "none");
        if (string.IsNullOrWhiteSpace(options.GitHub.Token))
            throw new InvalidOperationException("The enabled GitHub agent capability has no credential.");

        return new(access == "write", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GH_TOKEN"] = options.GitHub.Token
        }, access);
    }
}
