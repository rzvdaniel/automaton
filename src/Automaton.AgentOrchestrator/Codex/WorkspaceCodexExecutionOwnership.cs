using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workspaces;

namespace Automaton.AgentOrchestrator.Codex;

public sealed class WorkspaceCodexExecutionOwnership(WorkspaceExecutionContext context) : ICodexExecutionOwnership
{
    public async Task AcquireAsync(string launchNonce, CancellationToken cancellationToken)
    {
        await context.SetOwnershipAsync(WorkspaceExecutionOwnershipStates.Active, launchNonce, cancellationToken);
    }

    public async Task RecordProcessIdentityAsync(DurableExecutionIdentity identity, CancellationToken cancellationToken)
    {
        await context.RecordExecutionIdentityAsync(identity, cancellationToken);
    }

    public async Task ReleaseAsync(string launchNonce, CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Workspace.LaunchNonce, launchNonce, StringComparison.Ordinal)) throw new WorkspaceOwnershipException("Codex launch nonce does not match workspace ownership.");
        await context.SetOwnershipAsync(WorkspaceExecutionOwnershipStates.None, launchNonce, cancellationToken);
    }

    public async Task QuarantineAsync(string launchNonce, CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Workspace.LaunchNonce, launchNonce, StringComparison.Ordinal)) throw new WorkspaceOwnershipException("Codex launch nonce does not match workspace ownership.");
        await context.SetOwnershipAsync(WorkspaceExecutionOwnershipStates.Quarantined, launchNonce, cancellationToken);
    }
}
