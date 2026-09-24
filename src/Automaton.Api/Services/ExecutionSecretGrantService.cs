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
        CancellationToken cancellationToken,
        string deliveryPolicy = SecretDeliveryPolicies.SingleDelivery)
    {
        ValidateScope(scope);
        if (!EnvironmentTargetRegex().IsMatch(targetName))
            throw new ArgumentException("The environment-variable target is invalid.", nameof(targetName));
        var now = DateTime.UtcNow;
        if (expiresAtUtc <= now)
            throw new ArgumentException("The grant expiry must be in the future.", nameof(expiresAtUtc));
        if (deliveryPolicy is not (SecretDeliveryPolicies.SingleDelivery or SecretDeliveryPolicies.RetryableDelivery))
            throw new ArgumentException("The secret delivery policy is invalid.", nameof(deliveryPolicy));

        var version = await db.SecretVersions
            .Include(x => x.SecretDefinition)
            .FirstOrDefaultAsync(x => x.Id == secretVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("The secret version was not found.");
        ValidateVersionIdentity(version, scope);
        if (IsVersionInactive(version, now))
            throw new UnauthorizedAccessException("The secret version is inactive.");
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
            DeliveryPolicy = deliveryPolicy,
            TargetName = targetName,
            MaximumReads = deliveryPolicy == SecretDeliveryPolicies.SingleDelivery ? 1 : MaximumResolutionAttempts,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        };
        db.ExecutionSecretGrants.Add(grant);
        await db.SaveChangesAsync(cancellationToken);
        return grant.Id;
    }

    public Task<ExecutionSecretLease> LeaseAsync(
        SecretGrantScope scope,
        IReadOnlyCollection<Guid> grantIds,
        CancellationToken cancellationToken) => LeaseInternalAsync(scope, grantIds, cancellationToken, classifyUnavailable: false);

    public Task<ExecutionSecretLease> LeaseForInfrastructureCommandAsync(
        SecretGrantScope scope,
        IReadOnlyCollection<Guid> grantIds,
        CancellationToken cancellationToken) => LeaseInternalAsync(scope, grantIds, cancellationToken, classifyUnavailable: true);

    private async Task<ExecutionSecretLease> LeaseInternalAsync(
        SecretGrantScope scope,
        IReadOnlyCollection<Guid> grantIds,
        CancellationToken cancellationToken,
        bool classifyUnavailable)
    {
        ValidateScope(scope);
        var ids = NormalizeGrantIds(grantIds);

        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational() && db.Database.CurrentTransaction == null)
            transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var grants = await LoadGrantsAsync(ids, classifyUnavailable, cancellationToken);
            var now = DateTime.UtcNow;
            ValidateLoadedGrants(grants, scope, now, classifyUnavailable);
            await ClaimGrantsAsync(grants, now, cancellationToken, classifyUnavailable);
            var values = UnprotectValues(grants);
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

    private static Guid[] NormalizeGrantIds(IReadOnlyCollection<Guid> grantIds)
    {
        var ids = grantIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0 || ids.Length != grantIds.Count)
            throw new UnauthorizedAccessException("The secret grant references are invalid.");
        return ids;
    }

    private async Task<List<ExecutionSecretGrant>> LoadGrantsAsync(
        Guid[] ids,
        bool classifyUnavailable,
        CancellationToken cancellationToken)
    {
        var grants = await db.ExecutionSecretGrants
            .Include(x => x.SecretVersion)
                .ThenInclude(x => x.SecretDefinition)
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (grants.Count != ids.Length)
            throw classifyUnavailable
                ? new SecretGrantUnavailableException(HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch, ambiguous: false)
                : new UnauthorizedAccessException("One or more secret grants were not found.");

        return grants;
    }

    private static void ValidateLoadedGrants(
        IReadOnlyCollection<ExecutionSecretGrant> grants,
        SecretGrantScope scope,
        DateTime now,
        bool classifyUnavailable)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in grants)
        {
            ValidateGrant(grant, scope, now, classifyUnavailable);
            if (!targets.Add(grant.TargetName))
                throw classifyUnavailable
                    ? new SecretGrantUnavailableException(HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch, ambiguous: HasDeliveryEvidence(grant))
                    : new UnauthorizedAccessException("Secret grants contain duplicate delivery targets.");
        }

    }

    private Task ClaimGrantsAsync(
        IReadOnlyCollection<ExecutionSecretGrant> grants,
        DateTime now,
        CancellationToken cancellationToken,
        bool classifyUnavailable)
        => db.Database.IsRelational()
            ? ClaimRelationalGrantsAsync(grants, now, cancellationToken, classifyUnavailable)
            : UpdateTrackedGrantsAsync(grants, now, cancellationToken);

    private async Task ClaimRelationalGrantsAsync(
        IReadOnlyCollection<ExecutionSecretGrant> grants,
        DateTime now,
        CancellationToken cancellationToken,
        bool classifyUnavailable)
    {
        var leaseExpiresAtUtc = now.Add(LeaseDuration);
        foreach (var grant in grants)
        {
            var claimed = await db.ExecutionSecretGrants
                .Where(x => x.Id == grant.Id
                    && x.ConsumedAtUtc == null
                    && x.RevokedAtUtc == null
                    && x.ExpiresAtUtc > now
                    && (x.DeliveryPolicy != SecretDeliveryPolicies.SingleDelivery || x.DeliveredAtUtc == null)
                    && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= now)
                    && x.ReadCount < x.MaximumReads)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LeasedAtUtc, now)
                    .SetProperty(x => x.LeaseExpiresAtUtc, leaseExpiresAtUtc)
                    .SetProperty(x => x.DeliveredAtUtc, x => x.DeliveryPolicy == SecretDeliveryPolicies.SingleDelivery ? now : x.DeliveredAtUtc)
                    .SetProperty(x => x.ReadCount, x => x.ReadCount + 1),
                    cancellationToken);
            if (claimed != 1)
                throw classifyUnavailable
                    ? new SecretGrantUnavailableException(HostedRunnerDeploymentErrors.AmbiguousDispatch, ambiguous: true)
                    : new UnauthorizedAccessException("A secret grant could not be leased.");

            MarkTrackedGrantAsClaimed(grant, now, leaseExpiresAtUtc);
        }
    }

    private async Task UpdateTrackedGrantsAsync(
        IReadOnlyCollection<ExecutionSecretGrant> grants,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var leaseExpiresAtUtc = now.Add(LeaseDuration);
        foreach (var grant in grants)
            MarkTrackedGrantAsClaimed(grant, now, leaseExpiresAtUtc);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void MarkTrackedGrantAsClaimed(
        ExecutionSecretGrant grant,
        DateTime now,
        DateTime leaseExpiresAtUtc)
    {
        grant.LeasedAtUtc = now;
        grant.LeaseExpiresAtUtc = leaseExpiresAtUtc;
        if (grant.DeliveryPolicy == SecretDeliveryPolicies.SingleDelivery)
            grant.DeliveredAtUtc = now;
        grant.ReadCount++;
    }

    private Dictionary<string, string> UnprotectValues(IEnumerable<ExecutionSecretGrant> grants)
        => grants.ToDictionary(
            grant => grant.TargetName,
            grant => protector.Unprotect(grant.SecretVersion.ProtectedValue),
            StringComparer.Ordinal);

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
            if (grant.ConsumedAtUtc.HasValue)
                continue;
            if (!grant.LeasedAtUtc.HasValue || grant.RevokedAtUtc.HasValue)
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

    private static void ValidateGrant(ExecutionSecretGrant grant, SecretGrantScope scope, DateTime now, bool classifyUnavailable)
    {
        ValidateGrantScope(grant, scope);
        ValidateGrantDeliveryPolicy(grant, classifyUnavailable);
        ValidateGrantAvailability(grant, now, classifyUnavailable);
        ValidateVersionIdentity(grant.SecretVersion, scope);
        ValidateSecretVersionActive(grant, now, classifyUnavailable);
    }

    private static void ValidateGrantScope(ExecutionSecretGrant grant, SecretGrantScope scope)
    {
        if (grant.OperationId != scope.OperationId
            || grant.AppId != scope.AppId
            || !string.Equals(grant.ActionKey, scope.ActionKey, StringComparison.Ordinal)
            || grant.RunnerId != scope.RunnerId
            || grant.RunnerHostId != scope.RunnerHostId)
            throw new UnauthorizedAccessException("A secret grant does not match the requested execution scope.");
        if (!string.Equals(grant.DeliveryType, SecretDeliveryTypes.EnvironmentVariable, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The secret delivery type is not supported.");
    }

    private static void ValidateGrantDeliveryPolicy(ExecutionSecretGrant grant, bool classifyUnavailable)
    {
        if (grant.DeliveryPolicy == SecretDeliveryPolicies.SingleDelivery && grant.DeliveredAtUtc.HasValue)
            throw CreateUnavailable(classifyUnavailable, HostedRunnerDeploymentErrors.AmbiguousDispatch, ambiguous: true,
                "A single-delivery secret grant was already disclosed.");
    }

    private static void ValidateGrantAvailability(ExecutionSecretGrant grant, DateTime now, bool classifyUnavailable)
    {
        if (grant.ExpiresAtUtc <= now || grant.RevokedAtUtc.HasValue || grant.ConsumedAtUtc.HasValue)
            throw CreateUnavailable(classifyUnavailable,
                HasDeliveryEvidence(grant) ? HostedRunnerDeploymentErrors.AmbiguousDispatch : HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch,
                HasDeliveryEvidence(grant), "A secret grant is expired, revoked, or already consumed.");
        if (grant.LeaseExpiresAtUtc > now)
            throw CreateUnavailable(classifyUnavailable, HostedRunnerDeploymentErrors.AmbiguousDispatch, ambiguous: true,
                "A secret grant is already leased.");
        if (grant.ReadCount >= grant.MaximumReads)
            throw CreateUnavailable(classifyUnavailable,
                HasDeliveryEvidence(grant) ? HostedRunnerDeploymentErrors.AmbiguousDispatch : HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch,
                HasDeliveryEvidence(grant), "A secret grant exceeded its maximum resolution attempts.");
    }

    private static void ValidateSecretVersionActive(ExecutionSecretGrant grant, DateTime now, bool classifyUnavailable)
    {
        if (IsVersionInactive(grant.SecretVersion, now))
        {
            var hasDeliveryEvidence = HasDeliveryEvidence(grant);
            throw CreateUnavailable(classifyUnavailable,
                hasDeliveryEvidence ? HostedRunnerDeploymentErrors.AmbiguousDispatch : HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch,
                hasDeliveryEvidence, "The secret version is inactive.");
        }
    }

    private static Exception CreateUnavailable(bool classifyUnavailable, string reason, bool ambiguous, string message)
        => classifyUnavailable ? new SecretGrantUnavailableException(reason, ambiguous) : new UnauthorizedAccessException(message);

    private static bool HasDeliveryEvidence(ExecutionSecretGrant grant)
        => grant.DeliveredAtUtc.HasValue || grant.LeasedAtUtc.HasValue || grant.ConsumedAtUtc.HasValue || grant.ReadCount > 0;

    private static void ValidateVersionIdentity(SecretVersion version, SecretGrantScope scope)
    {
        var definition = version.SecretDefinition;
        if (!string.Equals(definition.OwnerType, scope.OwnerType, StringComparison.Ordinal)
            || definition.OwnerId != scope.OwnerId)
            throw new UnauthorizedAccessException("The secret owner does not match the requested execution scope.");
    }

    private static bool IsVersionInactive(SecretVersion version, DateTime now)
        => version.SecretDefinition.DeletedAtUtc.HasValue
            || version.RevokedAtUtc.HasValue
            || version.ExpiresAtUtc.HasValue && version.ExpiresAtUtc.Value <= now;

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

public sealed class SecretGrantUnavailableException(string reason, bool ambiguous) : UnauthorizedAccessException(reason)
{
    public string Reason { get; } = reason;
    public bool Ambiguous { get; } = ambiguous;
}
