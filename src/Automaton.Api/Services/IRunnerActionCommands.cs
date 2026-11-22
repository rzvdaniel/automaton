namespace Automaton.Api.Services;

/// <summary>Durable Runner boundary used by deployment-target cleanup.</summary>
public interface IRunnerActionCommands
{
    Task<T> SendAsync<T>(Guid runnerId, string kind, object payload, CancellationToken token);

    /// <summary>Like <see cref="SendAsync{T}"/> but gives up with <see cref="RunnerNotRespondingException"/> after the Runner command timeout; the queued command is cancelled.</summary>
    Task<T> SendBoundedAsync<T>(Guid runnerId, string kind, object payload, CancellationToken token)
        => SendAsync<T>(runnerId, kind, payload, token);
}
