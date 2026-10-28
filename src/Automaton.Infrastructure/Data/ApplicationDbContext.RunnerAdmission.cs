using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Automaton.Api.Data;

public partial class ApplicationDbContext
{
    private Guid[] AssignedRunnerIds() => ChangeTracker.Entries<AppDeploymentTarget>()
        .Where(entry => entry.State == EntityState.Added
            || entry.State == EntityState.Modified && entry.Property(target => target.ExecutionRunnerId).IsModified)
        .Select(entry => entry.Entity.ExecutionRunnerId)
        .OfType<Guid>().Distinct().Order().ToArray();

    private IDbContextTransaction? OpenRunnerAssignmentAdmission()
    {
        var ids = AssignedRunnerIds();
        var transaction = ids.Length > 0 && Database.IsRelational() && Database.CurrentTransaction is null
            ? Database.BeginTransaction() : null;
        try
        {
            foreach (var id in ids)
            {
                if (IsNewRunnerAssignment(id)) continue;
                var allowed = Runners.Where(runner => runner.Id == id && runner.DeletionRequestedAtUtc == null);
                // A conditional UPDATE serializes assignment with removal preparation on the Runner row.
                var admitted = Database.IsRelational()
                    ? allowed.ExecuteUpdate(setters => setters.SetProperty(runner => runner.DeletionRequestedAtUtc, (DateTime?)null)) == 1
                    : allowed.Any();
                if (!admitted) throw new InvalidOperationException("The assigned Runner is missing or being removed.");
            }
            return transaction;
        }
        catch { transaction?.Dispose(); throw; }
    }

    private async Task<IDbContextTransaction?> OpenRunnerAssignmentAdmissionAsync(CancellationToken token)
    {
        var ids = AssignedRunnerIds();
        var transaction = ids.Length > 0 && Database.IsRelational() && Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(token) : null;
        try
        {
            foreach (var id in ids)
            {
                if (IsNewRunnerAssignment(id)) continue;
                var allowed = Runners.Where(runner => runner.Id == id && runner.DeletionRequestedAtUtc == null);
                var admitted = Database.IsRelational()
                    ? await allowed.ExecuteUpdateAsync(setters => setters.SetProperty(runner => runner.DeletionRequestedAtUtc, (DateTime?)null), token) == 1
                    : await allowed.AnyAsync(token);
                if (!admitted) throw new InvalidOperationException("The assigned Runner is missing or being removed.");
            }
            return transaction;
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task EnsureRunnerOperationAdmissionAsync(Guid runnerId, CancellationToken token)
    {
        var runner = Runners.Where(runner => runner.Id == runnerId && runner.DeletionRequestedAtUtc == null);
        if (Database.IsRelational())
        {
            var accepted = await runner.ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.DeletionRequestedAtUtc, (DateTime?)null), token);
            if (accepted > 0) return;
            // Keep the existing missing-Runner behavior, but use a current locking read for removal state.
            var removing = await Runners.Where(item => item.Id == runnerId).ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.DeletionRequestedAtUtc, item => item.DeletionRequestedAtUtc), token);
            if (removing == 0) return;
        }
        else if (!await Runners.AnyAsync(item => item.Id == runnerId && item.DeletionRequestedAtUtc != null, token)) return;
        throw new InvalidOperationException("The Runner is being removed and cannot accept new operations.");
    }

    private bool IsNewRunnerAssignment(Guid id)
    {
        var entry = ChangeTracker.Entries<Runner>().SingleOrDefault(entry => entry.Entity.Id == id);
        if (entry?.Entity.DeletionRequestedAtUtc is not null)
            throw new InvalidOperationException("The assigned Runner is being removed.");
        return entry?.State == EntityState.Added;
    }
}
