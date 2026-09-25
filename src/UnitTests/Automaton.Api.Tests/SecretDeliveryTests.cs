using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class SecretDeliveryTests
{
    [Test]
    public async Task CreateLeaseAndConsume_ProtectsValueAndEnforcesExactScope()
    {
        await using var scenario = await SecretDeliveryTestScenarios.CreateAsync(
            "hosted-runner-enrollment",
            "re_super-sensitive-enrollment-value",
            "AUTOMATON_ENROLLMENT_TOKEN");
        await AssertProtectedValueAsync(scenario);
        await AssertScopeConsumptionAsync(scenario);
    }

    private static async Task AssertProtectedValueAsync(SecretDeliveryScenario scenario)
    {
        var persisted = await scenario.Db.SecretVersions.SingleAsync();
        Assert.That(persisted.ProtectedValue, Is.Not.EqualTo(scenario.Plaintext));
        Assert.That(persisted.ProtectedValue, Does.Not.Contain(scenario.Plaintext));
    }

    private static async Task AssertScopeConsumptionAsync(SecretDeliveryScenario scenario)
    {
        var wrongScope = scenario.Scope with { RunnerId = Guid.NewGuid() };
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Grants.LeaseAsync(wrongScope, [scenario.GrantId], CancellationToken.None));

        var lease = await scenario.Grants.LeaseAsync(scenario.Scope, [scenario.GrantId], CancellationToken.None);
        Assert.That(lease.EnvironmentVariables[scenario.TargetName], Is.EqualTo(scenario.Plaintext));
        await scenario.Grants.MarkConsumedAsync(lease.GrantIds, CancellationToken.None);

        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Grants.LeaseAsync(scenario.Scope, [scenario.GrantId], CancellationToken.None));
        var persistedGrant = await scenario.Db.ExecutionSecretGrants.SingleAsync();
        Assert.That(persistedGrant.ConsumedAtUtc, Is.Not.Null);
        Assert.That(persistedGrant.ReadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Lease_RejectsWrongOwnerExpiredAndRevokedVersions()
    {
        await using var scenario = await SecretDeliveryTestScenarios.CreateAsync(
            "temporary-credential",
            "value-never-persisted-in-plaintext",
            "TEMPORARY_CREDENTIAL");

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => scenario.Grants.LeaseAsync(
            scenario.Scope with { OwnerId = Guid.NewGuid() }, [scenario.GrantId], CancellationToken.None));

        await scenario.Store.RevokeVersionAsync(scenario.SecretVersionId, CancellationToken.None);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Grants.LeaseAsync(scenario.Scope, [scenario.GrantId], CancellationToken.None));

        var persistedGrant = await scenario.Db.ExecutionSecretGrants.SingleAsync();
        persistedGrant.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        var persistedVersion = await scenario.Db.SecretVersions.SingleAsync();
        persistedVersion.RevokedAtUtc = null;
        await scenario.Db.SaveChangesAsync();
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Grants.LeaseAsync(scenario.Scope, [scenario.GrantId], CancellationToken.None));
    }

    [Test]
    public async Task Lease_PreventsConcurrentDeliveryAndAllowsRetryAfterLeaseExpiry()
    {
        await using var scenario = await SecretDeliveryTestScenarios.CreateAsync(
            "retryable-credential",
            "temporary-value",
            "RETRYABLE_CREDENTIAL",
            SecretDeliveryPolicies.RetryableDelivery);

        await scenario.Grants.LeaseAsync(scenario.Scope, [scenario.GrantId], CancellationToken.None);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Grants.LeaseAsync(scenario.Scope, [scenario.GrantId], CancellationToken.None));

        var persistedGrant = await scenario.Db.ExecutionSecretGrants.SingleAsync();
        persistedGrant.LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await scenario.Db.SaveChangesAsync();
        var retry = await scenario.Grants.LeaseAsync(scenario.Scope, [scenario.GrantId], CancellationToken.None);
        Assert.That(retry.EnvironmentVariables[scenario.TargetName], Is.EqualTo(scenario.Plaintext));
        Assert.That(persistedGrant.ReadCount, Is.EqualTo(2));
    }

    [Test]
    public async Task SingleDelivery_DoesNotResolveAgainAfterAmbiguousDispatch()
    {
        await using var scenario = await SecretDeliveryTestScenarios.CreateAsync(
            "single-disclosure",
            "single-delivery-value",
            "SINGLE_DELIVERY_CREDENTIAL",
            SecretDeliveryPolicies.SingleDelivery);

        await scenario.Grants.LeaseAsync(scenario.Scope, [scenario.GrantId], CancellationToken.None);
        var persistedGrant = await scenario.Db.ExecutionSecretGrants.SingleAsync();
        Assert.That(persistedGrant.DeliveredAtUtc, Is.Not.Null);
        Assert.That(persistedGrant.ConsumedAtUtc, Is.Null);
        persistedGrant.LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await scenario.Db.SaveChangesAsync();

        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Grants.LeaseAsync(scenario.Scope, [scenario.GrantId], CancellationToken.None));
        Assert.That(persistedGrant.ReadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Cleanup_RevokesExpiredGrantAndVersionWithoutDeletingMetadata()
    {
        await using var scenario = await SecretDeliveryTestScenarios.CreateAsync("expiring-secret", "cleanup-secret", "AUTOMATON_TOKEN");
        await SecretDeliveryTestScenarios.SeedExpiredGrantBatchAsync(scenario.Db);
        await SecretDeliveryTestScenarios.RunCleanupAsync(scenario.Db);
        var result = await SecretDeliveryTestScenarios.ReadCleanupResultAsync(scenario.Db);
        Assert.Multiple(() =>
        {
            Assert.That(result.VersionRevoked, Is.True);
            Assert.That(result.RevokedGrants, Is.EqualTo(501));
            Assert.That(result.SecretDefinitions, Is.EqualTo(1));
            Assert.That(result.SecretVersions, Is.EqualTo(1));
            Assert.That(result.TotalGrants, Is.EqualTo(501));
        });
    }

}
