using Automaton.Api.Data;
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
        await using var db = CreateDbContext();
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var store = new SecretStore(db, protector);
        var grants = new ExecutionSecretGrantService(db, protector);
        var ownerId = Guid.NewGuid();
        var scope = CreateScope(ownerId);
        const string plaintext = "re_super-sensitive-enrollment-value";

        var version = await store.CreateVersionAsync(
            new SecretOwner(SecretOwnerTypes.HostedRunner, ownerId),
            "hosted-runner-enrollment",
            plaintext,
            DateTime.UtcNow.AddMinutes(10),
            CancellationToken.None);
        var persisted = await db.SecretVersions.SingleAsync();
        Assert.That(persisted.ProtectedValue, Is.Not.EqualTo(plaintext));
        Assert.That(persisted.ProtectedValue, Does.Not.Contain(plaintext));

        var grantId = await grants.CreateGrantAsync(
            version.SecretVersionId,
            scope,
            "AUTOMATON_ENROLLMENT_TOKEN",
            DateTime.UtcNow.AddMinutes(5),
            CancellationToken.None);

        var wrongScope = scope with { RunnerId = Guid.NewGuid() };
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            grants.LeaseAsync(wrongScope, [grantId], CancellationToken.None));

        var lease = await grants.LeaseAsync(scope, [grantId], CancellationToken.None);
        Assert.That(lease.EnvironmentVariables["AUTOMATON_ENROLLMENT_TOKEN"], Is.EqualTo(plaintext));
        await grants.MarkConsumedAsync(lease.GrantIds, CancellationToken.None);

        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            grants.LeaseAsync(scope, [grantId], CancellationToken.None));
        var persistedGrant = await db.ExecutionSecretGrants.SingleAsync();
        Assert.That(persistedGrant.ConsumedAtUtc, Is.Not.Null);
        Assert.That(persistedGrant.ReadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Lease_RejectsWrongOwnerExpiredAndRevokedVersions()
    {
        await using var db = CreateDbContext();
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var store = new SecretStore(db, protector);
        var grants = new ExecutionSecretGrantService(db, protector);
        var ownerId = Guid.NewGuid();
        var scope = CreateScope(ownerId);
        var version = await store.CreateVersionAsync(
            new SecretOwner(SecretOwnerTypes.HostedRunner, ownerId),
            "temporary-credential",
            "value-never-persisted-in-plaintext",
            DateTime.UtcNow.AddMinutes(10),
            CancellationToken.None);
        var grantId = await grants.CreateGrantAsync(
            version.SecretVersionId,
            scope,
            "TEMPORARY_CREDENTIAL",
            DateTime.UtcNow.AddMinutes(5),
            CancellationToken.None);

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => grants.LeaseAsync(
            scope with { OwnerId = Guid.NewGuid() }, [grantId], CancellationToken.None));

        await store.RevokeVersionAsync(version.SecretVersionId, CancellationToken.None);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            grants.LeaseAsync(scope, [grantId], CancellationToken.None));

        var persistedGrant = await db.ExecutionSecretGrants.SingleAsync();
        persistedGrant.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        var persistedVersion = await db.SecretVersions.SingleAsync();
        persistedVersion.RevokedAtUtc = null;
        await db.SaveChangesAsync();
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            grants.LeaseAsync(scope, [grantId], CancellationToken.None));
    }

    [Test]
    public async Task Lease_PreventsConcurrentDeliveryAndAllowsRetryAfterLeaseExpiry()
    {
        await using var db = CreateDbContext();
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var store = new SecretStore(db, protector);
        var grants = new ExecutionSecretGrantService(db, protector);
        var ownerId = Guid.NewGuid();
        var scope = CreateScope(ownerId);
        var version = await store.CreateVersionAsync(
            new SecretOwner(SecretOwnerTypes.HostedRunner, ownerId),
            "retryable-credential",
            "temporary-value",
            DateTime.UtcNow.AddMinutes(10),
            CancellationToken.None);
        var grantId = await grants.CreateGrantAsync(
            version.SecretVersionId,
            scope,
            "RETRYABLE_CREDENTIAL",
            DateTime.UtcNow.AddMinutes(5),
            CancellationToken.None,
            SecretDeliveryPolicies.RetryableDelivery);

        await grants.LeaseAsync(scope, [grantId], CancellationToken.None);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            grants.LeaseAsync(scope, [grantId], CancellationToken.None));

        var persistedGrant = await db.ExecutionSecretGrants.SingleAsync();
        persistedGrant.LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        var retry = await grants.LeaseAsync(scope, [grantId], CancellationToken.None);
        Assert.That(retry.EnvironmentVariables["RETRYABLE_CREDENTIAL"], Is.EqualTo("temporary-value"));
        Assert.That(persistedGrant.ReadCount, Is.EqualTo(2));
    }

    [Test]
    public async Task SingleDelivery_DoesNotResolveAgainAfterAmbiguousDispatch()
    {
        await using var db = CreateDbContext();
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var store = new SecretStore(db, protector);
        var grants = new ExecutionSecretGrantService(db, protector);
        var ownerId = Guid.NewGuid();
        var scope = CreateScope(ownerId);
        var version = await store.CreateVersionAsync(
            new SecretOwner(SecretOwnerTypes.HostedRunner, ownerId),
            "single-disclosure",
            "single-delivery-value",
            DateTime.UtcNow.AddMinutes(10),
            CancellationToken.None);
        var grantId = await grants.CreateGrantAsync(
            version.SecretVersionId,
            scope,
            "SINGLE_DELIVERY_CREDENTIAL",
            DateTime.UtcNow.AddMinutes(5),
            CancellationToken.None,
            SecretDeliveryPolicies.SingleDelivery);

        await grants.LeaseAsync(scope, [grantId], CancellationToken.None);
        var persistedGrant = await db.ExecutionSecretGrants.SingleAsync();
        Assert.That(persistedGrant.DeliveredAtUtc, Is.Not.Null);
        Assert.That(persistedGrant.ConsumedAtUtc, Is.Null);
        persistedGrant.LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            grants.LeaseAsync(scope, [grantId], CancellationToken.None));
        Assert.That(persistedGrant.ReadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Cleanup_RevokesExpiredGrantAndVersionWithoutDeletingMetadata()
    {
        await using var db = CreateDbContext();
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var store = new SecretStore(db, protector);
        var grants = new ExecutionSecretGrantService(db, protector);
        var ownerId = Guid.NewGuid();
        var scope = CreateScope(ownerId);
        var version = await store.CreateVersionAsync(
            new SecretOwner(SecretOwnerTypes.HostedRunner, ownerId),
            "expiring-secret",
            "cleanup-secret",
            DateTime.UtcNow.AddMinutes(10),
            CancellationToken.None);
        await grants.CreateGrantAsync(
            version.SecretVersionId,
            scope,
            "AUTOMATON_TOKEN",
            DateTime.UtcNow.AddMinutes(5),
            CancellationToken.None);
        var persistedVersion = await db.SecretVersions.SingleAsync();
        var persistedGrant = await db.ExecutionSecretGrants.SingleAsync();
        persistedVersion.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        persistedGrant.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        using var services = new ServiceCollection()
            .AddSingleton(db)
            .BuildServiceProvider();
        var cleanup = new SecretGrantCleanupService(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SecretGrantCleanupService>.Instance);
        await cleanup.CleanupAsync(CancellationToken.None);

        Assert.That((await db.SecretVersions.SingleAsync()).RevokedAtUtc, Is.Not.Null);
        Assert.That((await db.ExecutionSecretGrants.SingleAsync()).RevokedAtUtc, Is.Not.Null);
        Assert.That(await db.SecretDefinitions.CountAsync(), Is.EqualTo(1));
        Assert.That(await db.SecretVersions.CountAsync(), Is.EqualTo(1));
        Assert.That(await db.ExecutionSecretGrants.CountAsync(), Is.EqualTo(1));
    }

    private static SecretGrantScope CreateScope(Guid ownerId) => new(
        Guid.NewGuid(),
        Automaton.Core.Models.InfrastructureAppTrust.PlatformAppId,
        "provision-hosted-runner",
        Guid.NewGuid(),
        Guid.NewGuid(),
        SecretOwnerTypes.HostedRunner,
        ownerId);

    private static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
