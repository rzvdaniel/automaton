namespace Automaton.Api.Services;

/// <summary>Durable Runner boundary used by deployment-target cleanup.</summary>
public interface IRunnerActionCommands
{
    Task<T> SendAsync<T>(Guid runnerId, string kind, object payload, CancellationToken token);
}
