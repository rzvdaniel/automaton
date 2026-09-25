using Automaton.Api.Entities;

namespace Automaton.Api.Services;

public sealed class AppFileRunnerResolver(IAppDeploymentTargetResolver targetResolver)
{
    internal async Task<string?> GetUnavailableErrorAsync(Guid ownerId, Guid appId, CancellationToken cancellationToken)
    {
        try
        {
            var target = await targetResolver.ResolveAsync(appId, null, ownerId, cancellationToken);
            var runner = target.ExecutionRunner!;
            return RunnerPresenceEvaluator.IsOnline(
                runner.ConnectionId,
                runner.LastHeartbeatAtUtc,
                DateTime.UtcNow)
                ? null
                : $"Runner '{runner.Name}' is not connected. Please start the runner and try again.";
        }
        catch (KeyNotFoundException)
        {
            return "The configured app or deployment target no longer exists.";
        }
        catch (UnauthorizedAccessException)
        {
            return "The runner configured for this app is not accessible by this app's owner.";
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    internal async Task<Runner?> ResolveOnlineRunnerAsync(Guid ownerId, Guid appId, CancellationToken cancellationToken)
    {
        try
        {
            var target = await targetResolver.ResolveAsync(appId, null, ownerId, cancellationToken);
            var runner = target.ExecutionRunner!;
            return RunnerPresenceEvaluator.IsOnline(
                runner.ConnectionId,
                runner.LastHeartbeatAtUtc,
                DateTime.UtcNow)
                ? runner
                : null;
        }
        catch (Exception exception) when (
            exception is KeyNotFoundException or
                UnauthorizedAccessException or
                InvalidOperationException)
        {
            return null;
        }
    }
}
