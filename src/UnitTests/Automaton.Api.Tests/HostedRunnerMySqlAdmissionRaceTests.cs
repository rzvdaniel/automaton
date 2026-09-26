using Automaton.Api.Data;
using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace Automaton.Api.Tests;

[TestFixture]
[Category("MySqlIntegration")]
public sealed class HostedRunnerMySqlAdmissionRaceTests
{
    [Test]
    public async Task ConcurrentAdmissionAndStorageAdoptionSerializeOnTheAccountRow()
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable("AUTOMATON_TEST_MYSQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
            Assert.Ignore("Set AUTOMATON_TEST_MYSQL_CONNECTION_STRING to run the MySQL concurrency integration test.");

        await using var context = CreateContext(configuredConnectionString);
        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
        Assert.That(pendingMigrations, Is.Empty,
            "The MySQL race test requires a dedicated database already migrated to the current schema.");
        await VerifyConcurrentAdmissionAsync(context, configuredConnectionString);
        await VerifyConcurrentStorageAdoptionAsync(context, configuredConnectionString);
    }

    private static async Task VerifyConcurrentAdmissionAsync(ApplicationDbContext db, string connectionString)
    {
        var userId = Guid.NewGuid();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db, Guid.NewGuid().ToString("N")[..8]);
        HostedRunnerAuditTestData.ConfigureSpecificationResources(db, planId);
        HostedRunnerAuditTestData.SeedUser(db, userId, planId);
        HostedRunnerMySqlSeedData.SeedInfrastructureRunnerHost(db);

        var preparations = await RunConcurrentlyAsync(
            () => PrepareAsync(connectionString, userId),
            () => PrepareAsync(connectionString, userId));

        Assert.That(preparations.Select(result => result.HostedRunner.Id).Distinct().ToArray(), Has.Length.EqualTo(1));
        Assert.That(await db.HostedRunners.CountAsync(runner => runner.UserId == userId && runner.DeletedAt == null), Is.EqualTo(1));
    }

    private static async Task<HostedRunnerPreparationResult> PrepareAsync(string connectionString, Guid userId)
    {
        await using var db = CreateContext(connectionString);
        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db, userId, new HostedRunnerOptions { Enabled = false });
        return await service.PrepareProvisioningAsync(userId, Guid.Empty, forceProvision: true);
    }

    private static async Task VerifyConcurrentStorageAdoptionAsync(ApplicationDbContext db, string connectionString)
    {
        var userId = Guid.NewGuid();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db, Guid.NewGuid().ToString("N")[..8]);
        HostedRunnerAuditTestData.SeedUser(db, userId, planId);
        var firstHostId = Guid.NewGuid();
        var secondHostId = Guid.NewGuid();
        db.AccountStorageAllocations.Add(new AccountStorageAllocation
        {
            AccountId = userId,
            StorageProjectId = Random.Shared.Next(2_000_000, int.MaxValue)
        });
        await db.SaveChangesAsync();

        var adoptionResults = await RunConcurrentlyAsync(
            () => AdoptAsync(connectionString, userId, firstHostId),
            () => AdoptAsync(connectionString, userId, secondHostId));
        var winner = adoptionResults.Single(result => result.Accepted).RunnerHostId;
        var persistedHostId = await db.AccountStorageAllocations
            .Where(allocation => allocation.AccountId == userId)
            .Select(allocation => allocation.StorageRunnerHostId)
            .SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(adoptionResults.Count(result => result.Accepted), Is.EqualTo(1));
            Assert.That(adoptionResults.Count(result => !result.Accepted), Is.EqualTo(1));
            Assert.That(persistedHostId, Is.EqualTo(winner));
        });
    }

    private static async Task<StorageAdoptionResult> AdoptAsync(string connectionString, Guid userId, Guid hostId)
    {
        await using var db = CreateContext(connectionString);
        var localityResolver = new HostedRunnerStorageLocalityResolver(db);
        var adoption = new HostedRunnerStorageLocalityAdoptionService(db, localityResolver);
        try
        {
            await adoption.AdoptRunnerHostAsync(userId, hostId, CancellationToken.None);
            return new StorageAdoptionResult(true, hostId);
        }
        catch (InvalidOperationException)
        {
            return new StorageAdoptionResult(false, null);
        }
    }

    private static async Task<T[]> RunConcurrentlyAsync<T>(params Func<Task<T>>[] operations)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = operations.Select(async operation =>
        {
            await start.Task;
            return await operation();
        }).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    private static ApplicationDbContext CreateContext(string connectionString)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)))
            .Options);

    private sealed record StorageAdoptionResult(bool Accepted, Guid? RunnerHostId);

}
