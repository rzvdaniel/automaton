namespace Automaton.Api.Services;

public sealed class AppDeploymentTargetConflictException(string message)
    : InvalidOperationException(message);
