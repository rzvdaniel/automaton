namespace Automaton.Runner.Console.Services;

public interface IApplicationContainerCleanupRuntime
{
    Task RemoveTargetContainersAsync(ApplicationContainerScope scope, CancellationToken cancellationToken = default);
}

