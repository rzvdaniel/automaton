using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workspaces;

namespace Automaton.AgentOrchestrator.Codex;

public sealed class WorkspaceCodexExecutionOwnership(IWorkspaceManager workspaceManager, Workspace workspace) : ICodexExecutionOwnership
{
    private Workspace current = workspace;
    public Workspace? CurrentWorkspace => current;
    public void AdoptWorkspace(Workspace workspace) => current = workspace;

    public async Task AcquireAsync(string launchNonce, CancellationToken cancellationToken)
    {
        current = await workspaceManager.UpdateExecutionOwnershipAsync(current, WorkspaceExecutionOwnershipStates.Active, launchNonce, cancellationToken);
    }

    public async Task RecordProcessIdentityAsync(DurableExecutionIdentity identity, CancellationToken cancellationToken)
    {
        current = await workspaceManager.RecordExecutionIdentityAsync(current, identity, cancellationToken);
    }

    public async Task ReleaseAsync(string launchNonce, CancellationToken cancellationToken)
    {
        if (!string.Equals(current.LaunchNonce, launchNonce, StringComparison.Ordinal)) throw new WorkspaceOwnershipException("Codex launch nonce does not match workspace ownership.");
        current = await workspaceManager.UpdateExecutionOwnershipAsync(current, WorkspaceExecutionOwnershipStates.None, launchNonce, cancellationToken);
    }

    public async Task QuarantineAsync(string launchNonce, CancellationToken cancellationToken)
    {
        if (!string.Equals(current.LaunchNonce, launchNonce, StringComparison.Ordinal)) throw new WorkspaceOwnershipException("Codex launch nonce does not match workspace ownership.");
        current = await workspaceManager.UpdateExecutionOwnershipAsync(current, WorkspaceExecutionOwnershipStates.Quarantined, launchNonce, cancellationToken);
    }
}
