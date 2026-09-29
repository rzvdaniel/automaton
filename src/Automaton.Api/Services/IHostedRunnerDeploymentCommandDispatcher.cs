using Automaton.Api.Entities;

namespace Automaton.Api.Services;

public interface IHostedRunnerDeploymentCommandDispatcher
{
    Task<bool> PublishAsync(DeploymentOperation operation, Guid executionAttemptId, CancellationToken cancellationToken);
}
