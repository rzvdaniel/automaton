namespace Automaton.Api.Services;

public sealed class HostedRunnerDeploymentPublishException(Guid operationId, Exception innerException)
    : Exception($"Could not durably publish Hosted Runner deployment operation '{operationId}'.", innerException);
