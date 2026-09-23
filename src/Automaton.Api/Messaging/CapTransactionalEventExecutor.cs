using Automaton.Api.Data;
using Automaton.Messaging.Abstractions;
using DotNetCore.CAP;

namespace Automaton.Api.Messaging;

public sealed class CapTransactionalEventExecutor(
    ApplicationDbContext dataContext,
    ICapPublisher capPublisher) : ITransactionalEventExecutor
{
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var transaction = dataContext.Database.BeginTransaction(capPublisher, autoCommit: false);
        try
        {
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
