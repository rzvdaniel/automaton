namespace Automaton.Api.Services;

public sealed class HostedRunnerProvisioningConflictException(string message)
    : InvalidOperationException(message);
