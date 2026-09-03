namespace Automaton.Api.Services;

/// <summary>
/// Indicates that a deployment operation is still owned by a live executor.
/// CAP must treat this as a failed delivery so it retries rather than
/// acknowledging the message permanently.
/// </summary>
public sealed class DeploymentOperationLeaseActiveException(Guid operationId)
    : InvalidOperationException($"Deployment operation '{operationId}' is still owned by a live executor.");
