using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Automaton.Worker.Tests;

internal sealed class ActionScheduleTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly DbContextOptions<ApplicationDbContext> options;

    private ActionScheduleTestDatabase(SqliteConnection connection, DbContextOptions<ApplicationDbContext> options)
    {
        this.connection = connection;
        this.options = options;
    }

    internal static async Task<ActionScheduleTestDatabase> CreateAsync(
        ActionSchedule? schedule = null,
        SaveChangesInterceptor? interceptor = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection);
        if (interceptor != null) builder.AddInterceptors(interceptor);
        var options = builder.Options;
        await using var setup = new ApplicationDbContext(options);
        await CreateTablesAsync(setup);
        if (schedule != null)
        {
            setup.ActionSchedules.Add(schedule);
            await setup.SaveChangesAsync();
        }
        return new ActionScheduleTestDatabase(connection, options);
    }

    internal ApplicationDbContext OpenContext() => new(options);

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    private static Task CreateTablesAsync(ApplicationDbContext db)
        => db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE ActionSchedules (
                Id TEXT PRIMARY KEY, OwnerType TEXT NOT NULL, OwnerUserId TEXT NULL, SystemIdentity TEXT NULL,
                ApplicationId TEXT NOT NULL, ActionKey TEXT NOT NULL, Name TEXT NOT NULL,
                Description TEXT NULL, ExecutionScope TEXT NOT NULL, ScheduleType TEXT NOT NULL,
                RunAtUtc TEXT NULL, IntervalSeconds INTEGER NULL, CronExpression TEXT NULL,
                TimeZone TEXT NOT NULL, Enabled INTEGER NOT NULL, NextRunAtUtc TEXT NULL,
                LastTriggeredAtUtc TEXT NULL, LastExecutionId TEXT NULL, InputJson TEXT NOT NULL,
                ConcurrencyPolicy TEXT NOT NULL, MisfirePolicy TEXT NOT NULL,
                LeaseOwner TEXT NULL, LeaseExpiresAtUtc TEXT NULL, CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL);
            CREATE TABLE ActionScheduleOccurrences (
                Id TEXT PRIMARY KEY, ScheduleId TEXT NOT NULL, ScheduledForUtc TEXT NOT NULL,
                TriggeredAtUtc TEXT NOT NULL, ExecutionId TEXT NULL, Attempt INTEGER NOT NULL,
                IdempotencyKey TEXT NOT NULL, Status TEXT NOT NULL, FailureSummary TEXT NULL,
                CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);
            CREATE TABLE AppModuleExecutions (
                Id TEXT PRIMARY KEY, HostedRunnerOperationId TEXT NULL, ScheduleOccurrenceId TEXT NULL,
                AppId TEXT NOT NULL, DeploymentTargetId TEXT NULL, ModuleType TEXT NOT NULL, ModuleKey TEXT NOT NULL,
                RunnerId TEXT NULL, TriggerType TEXT NOT NULL, RequestedByUserId TEXT NULL, Status TEXT NOT NULL,
                DeploymentHash TEXT NULL, ExitCode INTEGER NULL, ErrorCode TEXT NULL, FailureSummary TEXT NULL,
                CreatedAtUtc TEXT NOT NULL, StartedAtUtc TEXT NULL, ExecutionDeadlineUtc TEXT NULL,
                CompletedAtUtc TEXT NULL, LogsAvailableUntilUtc TEXT NULL, ResultAvailableUntilUtc TEXT NULL,
                CorrelationId TEXT NULL, ResultFingerprint TEXT NULL);
            CREATE UNIQUE INDEX IX_AppModuleExecutions_ScheduleOccurrenceId ON AppModuleExecutions (ScheduleOccurrenceId);
            """);
}
