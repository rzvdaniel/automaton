using System.Data;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Automaton.Api.Services;

/// <summary>Atomically pins account storage to a host after validating all existing storage history.</summary>
public sealed class HostedRunnerStorageLocalityAdoptionService(
    ApplicationDbContext db,
    HostedRunnerStorageLocalityResolver localityResolver)
{
    public async Task<AccountStorageAllocation> AdoptRunnerHostAsync(
        Guid accountId,
        Guid runnerHostId,
        CancellationToken cancellationToken)
    {
        var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            await LockAccountAsync(accountId, cancellationToken);
            var allocation = await LoadAllocationAsync(accountId, cancellationToken);
            var resolvedHostId = await localityResolver.ResolveAsync(accountId, allocation, cancellationToken);
            EnsureHostIsCompatible(accountId, runnerHostId, resolvedHostId);
            await AdoptHostIfUnsetAsync(allocation, runnerHostId, cancellationToken);
            EnsureHostIsCompatible(accountId, runnerHostId, allocation.StorageRunnerHostId);
            await CommitAsync(transaction, cancellationToken);
            return allocation;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<AccountStorageAllocation> LoadAllocationAsync(Guid accountId, CancellationToken cancellationToken)
        => await db.AccountStorageAllocations.SingleOrDefaultAsync(
            allocation => allocation.AccountId == accountId,
            cancellationToken)
            ?? throw new InvalidOperationException("Account storage allocation was not found.");

    private async Task AdoptHostIfUnsetAsync(
        AccountStorageAllocation allocation,
        Guid runnerHostId,
        CancellationToken cancellationToken)
    {
        if (allocation.StorageRunnerHostId.HasValue)
            return;

        if (!db.Database.IsRelational())
        {
            allocation.StorageRunnerHostId = runnerHostId;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await db.AccountStorageAllocations
            .Where(item => item.AccountId == allocation.AccountId && item.StorageRunnerHostId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.StorageRunnerHostId, runnerHostId),
                cancellationToken);
        await db.Entry(allocation).ReloadAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
        => db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private async Task LockAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) != true)
            return;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT Id FROM AspNetUsers WHERE Id = {accountId} FOR UPDATE",
            cancellationToken);
    }

    private static void EnsureHostIsCompatible(Guid accountId, Guid requestedHostId, Guid? authoritativeHostId)
    {
        if (authoritativeHostId.HasValue && authoritativeHostId != requestedHostId)
        {
            throw new InvalidOperationException(
                $"Account '{accountId}' storage is pinned to Runner Host {authoritativeHostId}; " +
                $"the worker cannot adopt Runner Host {requestedHostId}. Storage migration is required before provisioning.");
        }
    }

    private static Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
        => transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);
}
