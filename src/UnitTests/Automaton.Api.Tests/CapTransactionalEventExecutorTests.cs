using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Messaging;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Tests;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using DotNetCore.CAP.Transport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Api.Tests;

public sealed class CapTransactionalEventExecutorTests
{
    [Test]
    public async Task CommitsDatabaseChangesWithoutPublishedEvent()
    {
        Assert.That(await CapTransactionScenario.RunAsync(), Is.True);
    }

    private sealed class CapTransactionScenario : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ApplicationDbContext dbContext;
        private readonly SubscriptionPlan plan;
        private readonly RecordingCapPublisher publisher;
        private readonly CapTransactionalEventExecutor executor;

        private CapTransactionScenario(
            SqliteConnection connection,
            ApplicationDbContext dbContext,
            SubscriptionPlan plan,
            RecordingCapPublisher publisher,
            CapTransactionalEventExecutor executor)
        {
            this.connection = connection;
            this.dbContext = dbContext;
            this.plan = plan;
            this.publisher = publisher;
            this.executor = executor;
        }

        public static async Task<bool> RunAsync()
        {
            await using var scenario = await CreateAsync();
            await scenario.CommitPlanChangeAsync();
            return await scenario.IsCommittedAsync();
        }

        private static async Task<CapTransactionScenario> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var dbContext = await CreateDbContextAsync(connection);
            var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", IsDefault = false };
            dbContext.SubscriptionPlans.Add(plan);
            await dbContext.SaveChangesAsync();

            var publisher = CreatePublisher();
            return new CapTransactionScenario(
                connection,
                dbContext,
                plan,
                publisher,
                new CapTransactionalEventExecutor(dbContext, publisher));
        }

        private static RecordingCapPublisher CreatePublisher()
        {
            var services = new ServiceCollection()
                .AddSingleton<IDispatcher, NoOpCapDispatcher>()
                .BuildServiceProvider();
            return new RecordingCapPublisher(services);
        }

        private async Task CommitPlanChangeAsync()
        {
            await executor.ExecuteAsync(async cancellationToken =>
            {
                Assert.That(publisher.Transaction?.AutoCommit, Is.False);
                plan.IsDefault = true;
                await dbContext.SaveChangesAsync(cancellationToken);
            });
        }

        private async Task<bool> IsCommittedAsync()
        {
            dbContext.ChangeTracker.Clear();
            return (await dbContext.SubscriptionPlans.SingleAsync(item => item.Id == plan.Id)).IsDefault;
        }

        private static async Task<ApplicationDbContext> CreateDbContextAsync(SqliteConnection connection)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "SubscriptionPlans" (
                "Id" TEXT NOT NULL PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Description" TEXT NULL,
                "PriceUsd" TEXT NOT NULL,
                "StripeProductId" TEXT NULL,
                "StripePriceId" TEXT NULL,
                "IsDefault" INTEGER NOT NULL DEFAULT 0,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CapabilitiesJson" TEXT NOT NULL,
                "SortOrder" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NULL
            );
            """);
            return dbContext;
        }

        public async ValueTask DisposeAsync()
        {
            await dbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NoOpCapDispatcher : IDispatcher
    {
        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueToPublish(MediumMessage message) => ValueTask.CompletedTask;
        public ValueTask EnqueueToExecute(MediumMessage message, ConsumerExecutorDescriptor? descriptor = null)
            => ValueTask.CompletedTask;
        public Task EnqueueToScheduler(MediumMessage message, DateTime scheduledTime, object? context)
            => Task.CompletedTask;
        public void Dispose()
        {
        }
    }
}
