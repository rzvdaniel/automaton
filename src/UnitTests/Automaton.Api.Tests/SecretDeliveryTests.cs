using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

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
            CancellationToken.None);

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
