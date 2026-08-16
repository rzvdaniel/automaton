using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Codex;

/// <summary>
/// Defines the narrowly scoped repository-workflow capability for a Codex child.
/// The current implementation maps the explicit GitHub workflow setting to the
/// standard gh credential, without exposing provider details to the supervisor.
/// </summary>
public sealed record CodexRuntimeCapability(
    bool RepositoryWorkflowEnabled,
    IReadOnlyDictionary<string, string> ChildEnvironment)
{
    private const string RepositoryWorkflowInstruction = """
Repository workflow capability is enabled for this task. Use the assigned branch only: commit the completed change, push that branch, and create or update one draft pull request targeting the configured base branch. Inspect checks and actionable review feedback for that draft pull request, and address it when the assigned task requires it. Never merge or approve a pull request, force-push, change the remote or base branch, or reveal credentials.
""";

    private const string NoRepositoryWorkflowInstruction = "Repository workflow capability is not enabled; do not attempt to publish or merge changes.";

    public string Instruction => RepositoryWorkflowEnabled ? RepositoryWorkflowInstruction : NoRepositoryWorkflowInstruction;

    public static CodexRuntimeCapability From(AgentExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.GitHub.Enabled) return new(false, new Dictionary<string, string>());
        if (string.IsNullOrWhiteSpace(options.GitHub.Token))
            throw new InvalidOperationException("The enabled GitHub agent capability has no credential.");

        return new(true, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GH_TOKEN"] = options.GitHub.Token
        });
    }
}
