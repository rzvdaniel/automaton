namespace Automaton.Messaging.Abstractions;

public interface ITransactionalEventExecutor
{
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
