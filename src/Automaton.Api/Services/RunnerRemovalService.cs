using System.Data;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Closes assignment admission before an owner uninstalls an empty Runner on its host.</summary>
public sealed class RunnerRemovalService(ApplicationDbContext db, RunnerDeletionPolicy policy)
{
    public async Task<RunnerRemovalInstructions> PrepareAsync(Guid id, CancellationToken token)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
        await policy.EnsureCanDeleteAsync(id, token);
        var runner = await db.Runners.SingleAsync(runner => runner.Id == id, token);
        var command = RunnerUninstallCommand.Build(id, runner.InstallationId);
        runner.DeletionRequestedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return new(id, command);
    }
}
