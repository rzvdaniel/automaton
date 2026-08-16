using Automaton.AgentOrchestrator.Domain;

namespace Automaton.AgentOrchestrator.Orchestration;

/// <summary>
/// Owns the one authoritative workspace snapshot for an execution. Every durable
/// workspace mutation replaces the current snapshot before the next mutation runs.
/// </summary>
public sealed class WorkspaceExecutionContext(IWorkspaceManager workspaces, Workspace workspace)
{
    public Workspace Workspace { get; private set; } = workspace ?? throw new ArgumentNullException(nameof(workspace));

    public async Task SetLifecycleAsync(string lifecycleStatus, CancellationToken cancellationToken)
        => Workspace = await workspaces.UpdateLifecycleAsync(Workspace, lifecycleStatus, cancellationToken);

    public async Task SetOwnershipAsync(string ownershipStatus, string launchNonce, CancellationToken cancellationToken)
        => Workspace = await workspaces.UpdateExecutionOwnershipAsync(Workspace, ownershipStatus, launchNonce, cancellationToken);

    public async Task RecordExecutionIdentityAsync(DurableExecutionIdentity identity, CancellationToken cancellationToken)
        => Workspace = await workspaces.RecordExecutionIdentityAsync(Workspace, identity, cancellationToken);

    public async Task SetRecoveryAsync(WorkspaceRecoveryState recoveryState, CancellationToken cancellationToken)
        => Workspace = await workspaces.UpdateRecoveryStateAsync(Workspace, recoveryState, cancellationToken);
}
