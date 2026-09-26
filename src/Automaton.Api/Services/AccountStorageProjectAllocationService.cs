using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public interface IAccountStorageProjectAllocationService
{
    Task<int> GetOrCreateAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<AccountStorageAllocation> GetOrCreateAllocationAsync(Guid accountId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves one persisted XFS project identity for a user account. Historical
/// Hosted Runner values are adopted only when they agree unambiguously.
/// </summary>
public sealed class AccountStorageProjectAllocationService(ApplicationDbContext db)
    : IAccountStorageProjectAllocationService
{
    private const int FirstProjectId = 1_000_000;

    public async Task<int> GetOrCreateAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var allocation = await db.AccountStorageAllocations
            .SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        var historicalProjectIds = await GetHistoricalProjectIdsAsync(accountId, cancellationToken);
        ValidateHistoricalProjectIds(accountId, historicalProjectIds);

        var existingProjectId = GetExistingProjectId(accountId, allocation, historicalProjectIds);
        if (existingProjectId.HasValue)
            return existingProjectId.Value;

        var adoptingHistoricalId = historicalProjectIds.Count == 1;
        var candidate = adoptingHistoricalId
            ? historicalProjectIds[0]
            : GetInitialCandidate(accountId);

        return await AllocateProjectIdAsync(accountId, candidate, adoptingHistoricalId, cancellationToken);
    }

    public async Task<AccountStorageAllocation> GetOrCreateAllocationAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await GetOrCreateAsync(accountId, cancellationToken);
        return await db.AccountStorageAllocations
            .SingleAsync(x => x.AccountId == accountId, cancellationToken);
    }

    private static void ValidateHistoricalProjectIds(Guid accountId, IReadOnlyCollection<int> historicalProjectIds)
    {
        if (historicalProjectIds.Any(x => x <= 0))
        {
            throw new InvalidOperationException(
                $"Account {accountId} has a Hosted Runner with an invalid storage project identity.");
        }

        if (historicalProjectIds.Count > 1)
        {
            throw new InvalidOperationException(
                $"Account {accountId} has conflicting historical Hosted Runner storage project identities.");
        }
    }

    private static int? GetExistingProjectId(
        Guid accountId,
        AccountStorageAllocation? allocation,
        IReadOnlyList<int> historicalProjectIds)
    {
        if (allocation is null)
            return null;

        if (allocation.StorageProjectId <= 0)
            throw new InvalidOperationException($"Account {accountId} has an invalid storage project identity.");

        if (historicalProjectIds.Count == 1 && historicalProjectIds[0] != allocation.StorageProjectId)
        {
            throw new InvalidOperationException(
                $"Account {accountId} has a Hosted Runner storage project identity that conflicts with its account allocation.");
        }

        return allocation.StorageProjectId;
    }

    private async Task<int> AllocateProjectIdAsync(
        Guid accountId,
        int candidate,
        bool adoptingHistoricalId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!adoptingHistoricalId && await IsProjectIdInUseAsync(candidate, cancellationToken))
            {
                candidate = NextCandidate(candidate);
                continue;
            }

            var allocation = new AccountStorageAllocation
            {
                AccountId = accountId,
                StorageProjectId = candidate
            };
            db.AccountStorageAllocations.Add(allocation);
            var persistedProjectId = await TryPersistAllocationAsync(
                allocation,
                candidate,
                adoptingHistoricalId,
                cancellationToken);
            if (persistedProjectId.HasValue)
                return persistedProjectId.Value;

            candidate = NextCandidate(candidate);
        }
    }

    private async Task<int?> TryPersistAllocationAsync(
        AccountStorageAllocation allocation,
        int candidate,
        bool adoptingHistoricalId,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return candidate;
        }
        catch (DbUpdateException ex) when (!adoptingHistoricalId)
        {
            db.Entry(allocation).State = EntityState.Detached;
            return await ResolveConcurrentAllocationAsync(allocation.AccountId, candidate, ex, cancellationToken);
        }
        catch (DbUpdateException ex) when (adoptingHistoricalId)
        {
            db.Entry(allocation).State = EntityState.Detached;
            return await ResolveHistoricalAllocationConflictAsync(allocation.AccountId, candidate, ex, cancellationToken);
        }
    }

    private async Task<int?> ResolveConcurrentAllocationAsync(
        Guid accountId,
        int candidate,
        DbUpdateException exception,
        CancellationToken cancellationToken)
    {
        var concurrentlyCreated = await db.AccountStorageAllocations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        if (concurrentlyCreated is not null)
            return await ValidateConcurrentAllocationAsync(accountId, concurrentlyCreated, cancellationToken);

        if (await IsProjectIdInUseAsync(candidate, cancellationToken))
            return null;

        ExceptionDispatchInfo.Capture(exception).Throw();
        return null;
    }

    private async Task<int> ValidateConcurrentAllocationAsync(
        Guid accountId,
        AccountStorageAllocation concurrentlyCreated,
        CancellationToken cancellationToken)
    {
        if (concurrentlyCreated.StorageProjectId <= 0
            || await HasConflictingHistoricalProjectAsync(
                accountId,
                concurrentlyCreated.StorageProjectId,
                cancellationToken))
        {
            throw new InvalidOperationException($"Account {accountId} has conflicting storage project identities.");
        }

        return concurrentlyCreated.StorageProjectId;
    }

    private async Task<int> ResolveHistoricalAllocationConflictAsync(
        Guid accountId,
        int candidate,
        DbUpdateException exception,
        CancellationToken cancellationToken)
    {
        var otherOwner = await db.AccountStorageAllocations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.StorageProjectId == candidate, cancellationToken);
        if (otherOwner?.AccountId == accountId)
            return candidate;

        if (otherOwner is not null)
        {
            throw new InvalidOperationException(
                $"Account {accountId}'s historical storage project identity {candidate} is already owned by another account.",
                exception);
        }

        ExceptionDispatchInfo.Capture(exception).Throw();
        return 0;
    }

    private Task<List<int>> GetHistoricalProjectIdsAsync(Guid accountId, CancellationToken cancellationToken)
        => db.HostedRunners
            .Where(x => x.UserId == accountId && x.StorageProjectId.HasValue)
            .Select(x => x.StorageProjectId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

    private async Task<bool> HasConflictingHistoricalProjectAsync(
        Guid accountId,
        int projectId,
        CancellationToken cancellationToken)
        => await db.HostedRunners.AnyAsync(
            x => x.UserId == accountId
                && x.StorageProjectId.HasValue
                && x.StorageProjectId.Value != projectId,
            cancellationToken);

    private async Task<bool> IsProjectIdInUseAsync(int projectId, CancellationToken cancellationToken)
        => await db.AccountStorageAllocations.AnyAsync(x => x.StorageProjectId == projectId, cancellationToken)
            || await db.HostedRunners.AnyAsync(x => x.StorageProjectId == projectId, cancellationToken);

    private static int GetInitialCandidate(Guid accountId)
    {
        var digest = SHA256.HashData(accountId.ToByteArray());
        return FirstProjectId + (int)(BitConverter.ToUInt32(digest, 0) % (uint)(int.MaxValue - FirstProjectId));
    }

    private static int NextCandidate(int current)
        => current >= int.MaxValue - 1 ? FirstProjectId : current + 1;
}
