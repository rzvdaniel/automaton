using Automaton.Api.Data;
using Automaton.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;

namespace Automaton.Tests.Shared;

/// <summary>
/// A migrated, throw-away PostgreSQL database for tests. Set AUTOMATON_TEST_POSTGRES_CONNECTION_STRING to a
/// server connection whose user may create databases and the citext extension; each fixture gets its own database.
/// </summary>
internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    internal const string ConnectionStringVariable = "AUTOMATON_TEST_POSTGRES_CONNECTION_STRING";
    private readonly string adminConnectionString;
    private readonly string databaseName;

    private PostgresTestDatabase(string adminConnectionString, string databaseName, string connectionString)
    {
        this.adminConnectionString = adminConnectionString;
        this.databaseName = databaseName;
        ConnectionString = connectionString;
    }

    internal string ConnectionString { get; }

    internal static async Task<PostgresTestDatabase> CreateAsync()
    {
        var admin = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(admin))
            Assert.Ignore($"Set {ConnectionStringVariable} to a PostgreSQL server for integration tests.");
        var name = $"automaton_test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync();
        }
        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
        var database = new PostgresTestDatabase(admin!, name, connectionString);
        await using var db = database.Open();
        await db.Database.MigrateAsync();
        return database;
    }

    internal ApplicationDbContext Open() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString, options => options.MigrationsAssembly(
                typeof(DependencyInjection).Assembly.GetName().Name)).Options);

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
