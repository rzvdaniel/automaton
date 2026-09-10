namespace Automaton.Api.Services;

public sealed class DeploymentOperationRunnerOwnershipChangedException(Guid runnerId)
    : InvalidOperationException($"Runner {runnerId} ownership changed during deployment.");
