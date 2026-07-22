using Automaton.Api.Data;
using Automaton.Messaging.Abstractions;
using DotNetCore.CAP;

namespace Automaton.Worker.Messaging;

public sealed class CapTransactionalEventExecutor(
    ApplicationDbContext dataContext,
    ICapPublisher capPublisher) : ITransactionalEventExecutor
{
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (dataContext.Database.BeginTransaction(capPublisher, autoCommit: true))
        {
            await operation(cancellationToken);
        }
    }
}
