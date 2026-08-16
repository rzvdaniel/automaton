using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Codex;

public sealed class CodexAppServerClientFactory(
    OrchestratorOptions options,
    IClock clock,
    IExecutionFence executionFence) : ICodexAppServerClientFactory
{
    public ICodexAppServerClient Create(WorkspaceExecutionContext context, IReadOnlyDictionary<string, string>? childEnvironment = null)
    {
        var workspace = context.Workspace;
        var transport = new ProcessCodexJsonRpcTransport(new(
            options.Codex.Command,
            workspace.Path,
            childEnvironment,
            TimeSpan.FromSeconds(5)), executionFence);
        return new CodexAppServerClient(
            transport,
            new WorkspaceCodexExecutionOwnership(context),
            clock,
            TimeSpan.FromMilliseconds(options.Codex.TurnTimeoutMilliseconds),
            workspace.Path);
    }
}
