using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Codex;

public sealed class CodexAppServerClientFactory(
    IWorkspaceManager workspaceManager,
    OrchestratorOptions options,
    IClock clock) : ICodexAppServerClientFactory
{
    public ICodexAppServerClient Create(Workspace workspace, IReadOnlyDictionary<string, string>? childEnvironment = null)
    {
        var transport = new ProcessCodexJsonRpcTransport(new(
            options.Codex.Command,
            workspace.Path,
            childEnvironment,
            TimeSpan.FromSeconds(5)));
        return new CodexAppServerClient(
            transport,
            new WorkspaceCodexExecutionOwnership(workspaceManager, workspace),
            clock,
            TimeSpan.FromMilliseconds(options.Codex.TurnTimeoutMilliseconds));
    }
}
