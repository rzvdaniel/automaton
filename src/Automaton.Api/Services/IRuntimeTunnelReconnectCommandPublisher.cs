namespace Automaton.Api.Services;

public interface IRuntimeTunnelReconnectCommandPublisher
{
    Task<int> PublishRuntimeTunnelReconnectsAsync(
        IReadOnlyCollection<Guid> runnerIds,
        string? targetApiInstanceId,
        string? targetApiColor,
        CancellationToken cancellationToken);
}
