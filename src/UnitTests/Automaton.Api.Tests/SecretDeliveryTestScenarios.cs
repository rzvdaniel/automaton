using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

internal static class SecretDeliveryTestScenarios
{
    public static async Task<SecretDeliveryScenario> CreateAsync(
        string secretName,
        string plaintext,
        string targetName,
        string deliveryPolicy = SecretDeliveryPolicies.SingleDelivery)
    {
        var db = CreateDbContext();
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var store = new SecretStore(db, protector);
        var grants = new ExecutionSecretGrantService(db, protector);
        var ownerId = Guid.NewGuid();
        var scope = CreateScope(ownerId);
        var version = await store.CreateVersionAsync(
            new SecretOwner(SecretOwnerTypes.HostedRunner, ownerId),
            secretName,
            plaintext,
            DateTime.UtcNow.AddMinutes(10),
            CancellationToken.None);
        var grantId = await grants.CreateGrantAsync(
            version.SecretVersionId,
            scope,
            targetName,
            DateTime.UtcNow.AddMinutes(5),
            CancellationToken.None,
            deliveryPolicy);
        return new SecretDeliveryScenario(db, store, grants, scope, version.SecretVersionId, grantId, plaintext, targetName);
    }

    public static async Task SeedExpiredGrantBatchAsync(ApplicationDbContext db)
    {
        var version = await db.SecretVersions.SingleAsync();
        var grant = await db.ExecutionSecretGrants.SingleAsync();
        version.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        grant.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        AddExpiredGrants(db, version.Id, grant, count: 500);
        await db.SaveChangesAsync();
    }

    public static async Task RunCleanupAsync(ApplicationDbContext db)
    {
        using var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var cleanup = new SecretGrantCleanupService(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SecretGrantCleanupService>.Instance);
        await cleanup.CleanupAsync(CancellationToken.None);
    }

    public static async Task<CleanupResult> ReadCleanupResultAsync(ApplicationDbContext db) => new(
        (await db.SecretVersions.SingleAsync()).RevokedAtUtc is not null,
        await db.ExecutionSecretGrants.CountAsync(x => x.RevokedAtUtc != null),
        await db.SecretDefinitions.CountAsync(),
        await db.SecretVersions.CountAsync(),
        await db.ExecutionSecretGrants.CountAsync());

    private static void AddExpiredGrants(ApplicationDbContext db, Guid secretVersionId, ExecutionSecretGrant template, int count)
    {
        for (var index = 0; index < count; index++)
        {
            db.ExecutionSecretGrants.Add(new ExecutionSecretGrant
            {
                Id = Guid.NewGuid(),
                OperationId = Guid.NewGuid(),
                SecretVersionId = secretVersionId,
                AppId = template.AppId,
                ActionKey = template.ActionKey,
                RunnerId = template.RunnerId,
                RunnerHostId = template.RunnerHostId,
                DeliveryType = template.DeliveryType,
                DeliveryPolicy = SecretDeliveryPolicies.SingleDelivery,
                TargetName = $"BATCHED_TOKEN_{index}",
                MaximumReads = 1,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1)
            });
        }
    }

    private static SecretGrantScope CreateScope(Guid ownerId) => new(
        Guid.NewGuid(),
        InfrastructureAppTrust.PlatformAppId,
        "restart-hosted-runner",
        Guid.NewGuid(),
        Guid.NewGuid(),
        SecretOwnerTypes.HostedRunner,
        ownerId);

    private static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}

internal sealed class SecretDeliveryScenario(
    ApplicationDbContext db,
    SecretStore store,
    ExecutionSecretGrantService grants,
    SecretGrantScope scope,
    Guid secretVersionId,
    Guid grantId,
    string plaintext,
    string targetName) : IAsyncDisposable
{
    public ApplicationDbContext Db { get; } = db;
    public SecretStore Store { get; } = store;
    public ExecutionSecretGrantService Grants { get; } = grants;
    public SecretGrantScope Scope { get; } = scope;
    public Guid SecretVersionId { get; } = secretVersionId;
    public Guid GrantId { get; } = grantId;
    public string Plaintext { get; } = plaintext;
    public string TargetName { get; } = targetName;

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}

internal sealed record CleanupResult(
    bool VersionRevoked,
    int RevokedGrants,
    int SecretDefinitions,
    int SecretVersions,
    int TotalGrants);
