using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal sealed class HostedRunnerRestartRetrySqliteFixture : IAsyncDisposable
{
    private HostedRunnerRestartRetrySqliteFixture(
        SqliteConnection connection,
        ApplicationDbContext db,
        Runner runner)
    {
        Connection = connection;
        Db = db;
        Runner = runner;
    }

    public SqliteConnection Connection { get; }
    public ApplicationDbContext Db { get; }
    public Runner Runner { get; }

    public static async Task<HostedRunnerRestartRetrySqliteFixture> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new HostedRunnerFailUpdateOnceInterceptor())
            .Options;
        var db = new ApplicationDbContext(options);
        await CreateSchemaAsync(db);

        var now = DateTime.UtcNow;
        var hostedRunnerId = Guid.NewGuid();
        var runner = new Runner { Id = Guid.NewGuid(), Name = "restart", ConnectionId = "new", Status = "online" };
        await SeedRestartAsync(db, hostedRunnerId, runner, now);
        return new HostedRunnerRestartRetrySqliteFixture(connection, db, runner);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await Connection.DisposeAsync();
    }

    private static Task CreateSchemaAsync(ApplicationDbContext db) => db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE HostedRunners (
            Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, Name TEXT NOT NULL,
            Status TEXT NOT NULL, LastError TEXT NULL, UpdatedAt TEXT NULL
        );
        CREATE TABLE HostedRunnerDeployments (
            Id TEXT NOT NULL PRIMARY KEY, HostedRunnerId TEXT NOT NULL, Action TEXT NOT NULL,
            Status TEXT NOT NULL, ExpectedRunnerId TEXT NULL, PreviousConnectionId TEXT NULL,
            ReconnectAfterUtc TEXT NULL, ReconnectDeadlineUtc TEXT NULL, StartedAt TEXT NOT NULL,
            FinishedAt TEXT NULL, Error TEXT NULL, Logs TEXT NULL
        );
        CREATE TABLE RunnerConnectionSessions (
            Id TEXT NOT NULL PRIMARY KEY, RunnerId TEXT NOT NULL, ConnectionId TEXT NOT NULL,
            ConnectedAtUtc TEXT NOT NULL
        );
        """);

    private static Task SeedRestartAsync(ApplicationDbContext db, Guid hostedRunnerId, Runner runner, DateTime now)
    {
        var operationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO HostedRunners (Id, UserId, Name, Status) VALUES ({hostedRunnerId}, {userId}, {"restart"}, {HostedRunnerStatuses.Failed});
            INSERT INTO HostedRunnerDeployments
                (Id, HostedRunnerId, Action, Status, ExpectedRunnerId, PreviousConnectionId, ReconnectAfterUtc, ReconnectDeadlineUtc, StartedAt)
            VALUES ({operationId}, {hostedRunnerId}, {HostedRunnerDeploymentActions.Restart}, {HostedRunnerDeploymentStatuses.WaitingForReconnect},
                {runner.Id}, {"old"}, {now.AddMinutes(-1)}, {now.AddMinutes(1)}, {now.AddMinutes(-2)});
            INSERT INTO RunnerConnectionSessions (Id, RunnerId, ConnectionId, ConnectedAtUtc)
            VALUES ({Guid.NewGuid()}, {runner.Id}, {runner.ConnectionId}, {now});
            """);
    }
}
