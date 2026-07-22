using System.Data;
using System.Text.RegularExpressions;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Automaton.Api.Services;

public sealed partial class ExecutionSecretGrantService(
    ApplicationDbContext db,
    ISecretProtector protector) : IExecutionSecretGrantService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private const int MaximumResolutionAttempts = 3;

    public async Task<Guid> CreateGrantAsync(
        Guid secretVersionId,
        SecretGrantScope scope,
        string targetName,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken)
    {
        ValidateScope(scope);
        if (!EnvironmentTargetRegex().IsMatch(targetName))
            throw new ArgumentException("The environment-variable target is invalid.", nameof(targetName));
        var now = DateTime.UtcNow;
        if (expiresAtUtc <= now)
            throw new ArgumentException("The grant expiry must be in the future.", nameof(expiresAtUtc));

        var version = await db.SecretVersions
            .Include(x => x.SecretDefinition)
            .FirstOrDefaultAsync(x => x.Id == secretVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("The secret version was not found.");
        ValidateVersion(version, scope, now);
        if (version.ExpiresAtUtc.HasValue && expiresAtUtc > version.ExpiresAtUtc.Value)
            throw new InvalidOperationException("The grant cannot outlive the secret version.");

        var grant = new ExecutionSecretGrant
        {
            Id = Guid.NewGuid(),
            OperationId = scope.OperationId,
            SecretVersionId = version.Id,
            AppId = scope.AppId,
            ActionKey = scope.ActionKey,
            RunnerId = scope.RunnerId,
            RunnerHostId = scope.RunnerHostId,
            DeliveryType = SecretDeliveryTypes.EnvironmentVariable,
            TargetName = targetName,
            MaximumReads = MaximumResolutionAttempts,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        };
        db.ExecutionSecretGrants.Add(grant);
        await db.SaveChangesAsync(cancellationToken);
        return grant.Id;
    }

    public async Task<ExecutionSecretLease> LeaseAsync(
        SecretGrantScope scope,
        IReadOnlyCollection<Guid> grantIds,
        CancellationToken cancellationToken)
    {
        ValidateScope(scope);
        var ids = grantIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0 || ids.Length != grantIds.Count)
            throw new UnauthorizedAccessException("The secret grant references are invalid.");

        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational() && db.Database.CurrentTransaction == null)
            transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var grants = await db.ExecutionSecretGrants
                .Include(x => x.SecretVersion)
                    .ThenInclude(x => x.SecretDefinition)
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken);
            if (grants.Count != ids.Length)
                throw new UnauthorizedAccessException("One or more secret grants were not found.");

            var now = DateTime.UtcNow;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var grant in grants)
            {
                ValidateGrant(grant, scope, now);
                if (!values.TryAdd(grant.TargetName, protector.Unprotect(grant.SecretVersion.ProtectedValue)))
                    throw new UnauthorizedAccessException("Secret grants contain duplicate delivery targets.");
                grant.LeasedAtUtc = now;
                grant.LeaseExpiresAtUtc = now.Add(LeaseDuration);
                grant.ReadCount++;
            }

            await db.SaveChangesAsync(cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
            return new(values, ids);
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    public async Task MarkConsumedAsync(
        IReadOnlyCollection<Guid> grantIds,
        CancellationToken cancellationToken)
    {
        var ids = grantIds.Distinct().ToArray();
        var grants = await db.ExecutionSecretGrants.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (grants.Count != ids.Length)
            throw new InvalidOperationException("One or more delivered grants no longer exist.");
        var now = DateTime.UtcNow;
        foreach (var grant in grants)
        {
            if (!grant.LeasedAtUtc.HasValue || grant.ConsumedAtUtc.HasValue || grant.RevokedAtUtc.HasValue)
                throw new InvalidOperationException("A delivered grant is not in a consumable state.");
            grant.ConsumedAtUtc = now;
            grant.LeaseExpiresAtUtc = null;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeByVersionAsync(Guid secretVersionId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var grants = await db.ExecutionSecretGrants
            .Where(x => x.SecretVersionId == secretVersionId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var grant in grants)
            grant.RevokedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateGrant(ExecutionSecretGrant grant, SecretGrantScope scope, DateTime now)
    {
        if (grant.OperationId != scope.OperationId
            || grant.AppId != scope.AppId
            || !string.Equals(grant.ActionKey, scope.ActionKey, StringComparison.Ordinal)
            || grant.RunnerId != scope.RunnerId
            || grant.RunnerHostId != scope.RunnerHostId)
            throw new UnauthorizedAccessException("A secret grant does not match the requested execution scope.");
        if (!string.Equals(grant.DeliveryType, SecretDeliveryTypes.EnvironmentVariable, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The secret delivery type is not supported.");
        if (grant.ExpiresAtUtc <= now || grant.RevokedAtUtc.HasValue || grant.ConsumedAtUtc.HasValue)
            throw new UnauthorizedAccessException("A secret grant is expired, revoked, or already consumed.");
        if (grant.LeaseExpiresAtUtc > now)
            throw new UnauthorizedAccessException("A secret grant is already leased.");
        if (grant.ReadCount >= grant.MaximumReads)
            throw new UnauthorizedAccessException("A secret grant exceeded its maximum resolution attempts.");
        ValidateVersion(grant.SecretVersion, scope, now);
    }

    private static void ValidateVersion(SecretVersion version, SecretGrantScope scope, DateTime now)
    {
        var definition = version.SecretDefinition;
        if (definition.DeletedAtUtc.HasValue
            || version.RevokedAtUtc.HasValue
            || version.ExpiresAtUtc.HasValue && version.ExpiresAtUtc.Value <= now)
            throw new UnauthorizedAccessException("The secret version is inactive.");
        if (!string.Equals(definition.OwnerType, scope.OwnerType, StringComparison.Ordinal)
            || definition.OwnerId != scope.OwnerId)
            throw new UnauthorizedAccessException("The secret owner does not match the requested execution scope.");
    }

    private static void ValidateScope(SecretGrantScope scope)
    {
        if (scope.OperationId == Guid.Empty || scope.AppId == Guid.Empty || scope.RunnerId == Guid.Empty
            || scope.OwnerId == Guid.Empty || string.IsNullOrWhiteSpace(scope.ActionKey)
            || string.IsNullOrWhiteSpace(scope.OwnerType))
            throw new ArgumentException("The secret grant scope is incomplete.", nameof(scope));
    }

    [GeneratedRegex("^[A-Z_][A-Z0-9_]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentTargetRegex();
}
