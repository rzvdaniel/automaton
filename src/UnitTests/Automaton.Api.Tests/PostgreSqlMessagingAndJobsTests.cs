using Automaton.Api.Data;
using Automaton.Api.Messaging;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Tests.Shared;
using DotNetCore.CAP;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DotNetCore.CAP.Persistence;
using Npgsql;

namespace Automaton.Api.Tests;

/// <summary>The transactional outbox and background jobs run on PostgreSQL storage.</summary>
[TestFixture]
public sealed class PostgreSqlMessagingAndJobsTests
{
    private static int jobRuns;
    private PostgresTestDatabase database = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase() => database = await PostgresTestDatabase.CreateAsync();

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        if (database is not null) await database.DisposeAsync();
    }

    [Test]
    public async Task CapEventPublishedInTransaction_IsStoredOnlyWhenTheTransactionCommits()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(database.ConnectionString));
        services.AddScoped<ITransactionalEventExecutor, CapTransactionalEventExecutor>();
        services.AddScoped<IEventPublisher, CapEventPublisher>();
        services.AddCap(options =>
        {
            CapRegistration.ConfigureStorage(options);
            // The broker is never contacted: CAP is not started, only its storage is initialized.
            options.UseRabbitMQ(rabbitMq => rabbitMq.HostName = "localhost");
        });
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);

        await using (var scope = provider.CreateAsyncScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<ITransactionalEventExecutor>();
            var events = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            await executor.ExecuteAsync(token => events.PublishAsync(Registered("committed"), token));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<ITransactionalEventExecutor>();
            var events = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(async token =>
            {
                await events.PublishAsync(Registered("rolled-back"), token);
                throw new InvalidOperationException("Roll back");
            }));
        }

        Assert.Multiple(async () =>
        {
            Assert.That(await PublishedCountAsync("committed"), Is.EqualTo(1));
            Assert.That(await PublishedCountAsync("rolled-back"), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task HangfireJob_RunsFromPostgreSqlStorage()
    {
        var before = Volatile.Read(ref jobRuns);
        var storageOptions = new PostgreSqlStorageOptions
        {
            PrepareSchemaIfNecessary = true, QueuePollInterval = TimeSpan.FromMilliseconds(200), SchemaName = "hangfire"
        };
        var storage = new PostgreSqlStorage(new NpgsqlConnectionFactory(database.ConnectionString, storageOptions), storageOptions);
        var client = new BackgroundJobClient(storage);
        using var server = new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 1, SchedulePollingInterval = TimeSpan.FromMilliseconds(200) }, storage);
        client.Enqueue(() => CountRun());

        for (var attempt = 0; attempt < 100 && Volatile.Read(ref jobRuns) == before; attempt++)
            await Task.Delay(100);

        Assert.That(Volatile.Read(ref jobRuns), Is.EqualTo(before + 1));
    }

    public static void CountRun() => Interlocked.Increment(ref jobRuns);

    private static UserRegisteredEvent Registered(string email)
        => new(Guid.NewGuid(), email, null, null, DateTime.UtcNow);

    private async Task<long> PublishedCountAsync(string email)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM cap_api.published WHERE \"Content\" LIKE @content", connection);
        command.Parameters.AddWithValue("content", $"%\"{email}\"%");
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
