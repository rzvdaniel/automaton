using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Security;
using Automaton.Api.Services;
using Automaton.Node.Enrollment;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace Automaton.Api.Tests;

internal sealed class NodeEnrollmentMySqlTestFixture : IAsyncDisposable
{
    public const string EnrollmentMigrationId = "20261012183817_AddNodeEnrollmentAuthentication";
    public const string EnrollmentToken = "synthetic-mysql-enrollment-token-for-tests";
    private readonly string administratorConnectionString;
    private readonly string databaseName;
    private readonly IDataProtectionProvider protectionProvider = new EphemeralDataProtectionProvider(NullLoggerFactory.Instance);

    private NodeEnrollmentMySqlTestFixture(string administratorConnectionString, string databaseName, string connectionString)
    {
        this.administratorConnectionString = administratorConnectionString;
        this.databaseName = databaseName;
        ConnectionString = connectionString;
        Options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(ConnectionString, new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;
    }

    public string ConnectionString { get; }
    public DbContextOptions<ApplicationDbContext> Options { get; }
    public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
    public Guid EnrollmentId { get; } = Guid.NewGuid();
    public Guid NodeId { get; } = Guid.NewGuid();
    public Guid InstallationId { get; } = Guid.NewGuid();
    public Guid RealmId { get; } = Guid.NewGuid();
    public NodeEnrollmentKeyMaterial KeyMaterial { get; private set; } = null!;

    public static async Task<NodeEnrollmentMySqlTestFixture> CreateAsync()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("AUTOMATON_TEST_MYSQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configuredConnection))
            Assert.Ignore("Requires AUTOMATON_TEST_MYSQL_CONNECTION_STRING and permission to create/drop an isolated test database.");

        var source = new MySqlConnectionStringBuilder(configuredConnection);
        if (string.IsNullOrWhiteSpace(source.Database))
            Assert.Ignore("The isolated MySQL test connection must name an existing database for authorization and server selection.");

        // Pomelo appends the database name to a MySQL advisory lock limited to 64 characters.
        var databaseName = "automaton_n1a" + Guid.NewGuid().ToString("N");
        var fixtureBuilder = new MySqlConnectionStringBuilder(source.ConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        var fixture = new NodeEnrollmentMySqlTestFixture(source.ConnectionString, databaseName, fixtureBuilder.ConnectionString);
        try
        {
            await using (var administrator = new MySqlConnection(source.ConnectionString))
            {
                await administrator.OpenAsync();
                await using var createDatabase = new MySqlCommand(
                    $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci",
                    administrator);
                await createDatabase.ExecuteNonQueryAsync();
            }

            fixture.KeyMaterial = NodeEnrollmentKeyMaterial.Create("CN=synthetic-mysql-node", fixture.Now);
            await NodeEnrollmentMySqlMigrationInstaller.ApplyAsync(fixture.CreateContext());
            return fixture;
        }
        catch
        {
            await fixture.DropDatabaseAsync();
            fixture.KeyMaterial?.Dispose();
            throw;
        }
    }

    public ApplicationDbContext CreateContext() => new(Options);

    public NodeEnrollmentService CreateService(ApplicationDbContext db) => new(
        db,
        protectionProvider,
        new FrozenTimeProvider(Now),
        new NodeProxyCertificateValidator(new StaticOptionsMonitor<NodeProxyAuthenticationOptions>(new NodeProxyAuthenticationOptions())));

    public NodeEnrollmentAuthorization CreatePendingAuthorization() => new()
    {
        Id = EnrollmentId,
        NodeId = NodeId,
        InstallationId = InstallationId,
        ControlPlaneRealmId = RealmId,
        OwnerScope = "synthetic/mysql-test-scope",
        PinnedPublicKeySha256 = KeyMaterial.PinnedPublicKeySha256,
        EnrollmentTokenSha256 = NodeEnrollmentCryptography.HashUtf8(EnrollmentToken),
        Status = NodeEnrollmentStatuses.Pending,
        ExpiresAtUtc = Now.AddHours(1).UtcDateTime
    };

    public NodeBootstrapRequest ValidBootstrapRequest() => new(
        EnrollmentToken,
        Convert.ToBase64String(KeyMaterial.CsrDer),
        Convert.ToBase64String(KeyMaterial.CertificateDer));

    public async Task<NodeEnrollmentReceipt> CommitEnrollmentAsync()
    {
        await using var db = CreateContext();
        db.NodeEnrollmentAuthorizations.Add(CreatePendingAuthorization());
        await db.SaveChangesAsync();
        var result = await CreateService(db).BootstrapAsync(EnrollmentId, ValidBootstrapRequest(), CancellationToken.None);
        Assert.That(result.Accepted, Is.True, "MySQL bootstrap setup must commit successfully.");
        return result.Receipt!;
    }

    public async Task<NodeEnrollmentChallengeResult> CreateChallengeAsync()
    {
        await using var db = CreateContext();
        return await CreateService(db).CreateRecoveryChallengeAsync(EnrollmentId, InstallationId, RealmId, CancellationToken.None);
    }

    public NodeRecoveryCompletionRequest SignChallenge(NodeEnrollmentChallengeResult challenge)
    {
        var message = NodeEnrollmentCryptography.BuildRecoveryMessage(
            challenge.ChallengeId,
            DecodeBase64Url(challenge.NonceBase64Url!),
            EnrollmentId,
            InstallationId,
            RealmId);
        var signature = KeyMaterial.SignRecoveryChallenge(message);
        return new NodeRecoveryCompletionRequest(
            challenge.ChallengeId,
            InstallationId,
            RealmId,
            Convert.ToBase64String(signature));
    }

    public async Task<NodeEnrollmentOperationResult> CompleteRecoveryAsync(NodeRecoveryCompletionRequest request)
    {
        await using var db = CreateContext();
        return await CreateService(db).CompleteRecoveryAsync(EnrollmentId, request, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        KeyMaterial?.Dispose();
        await DropDatabaseAsync();
    }

    private async Task DropDatabaseAsync()
    {
        try
        {
            await using var administrator = new MySqlConnection(administratorConnectionString);
            await administrator.OpenAsync();
            await using var dropDatabase = new MySqlCommand($"DROP DATABASE IF EXISTS `{databaseName}`", administrator);
            await dropDatabase.ExecuteNonQueryAsync();
        }
        catch (MySqlException)
        {
            // Preserve the original test/setup failure; the randomized schema is never a production database.
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }

    private sealed class FrozenTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
    }
}

internal static class NodeEnrollmentMySqlMigrationInstaller
{
    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        await using (db)
        {
            var migrations = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>().Migrations.Keys.ToArray();
            var enrollmentMigrationIndex = Array.IndexOf(migrations, NodeEnrollmentMySqlTestFixture.EnrollmentMigrationId);
            Assert.That(enrollmentMigrationIndex, Is.GreaterThanOrEqualTo(0), "The enrollment migration must be present in the API migration assembly.");

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE `__EFMigrationsHistory` (
                    `MigrationId` varchar(150) NOT NULL,
                    `ProductVersion` varchar(32) NOT NULL,
                    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
                ) CHARACTER SET=utf8mb4;
                """);
            const string productVersion = "9.0.10";
            for (var index = 0; index < enrollmentMigrationIndex; index++)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`) VALUES ({migrations[index]}, {productVersion})");
            }

            await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
                .MigrateAsync(NodeEnrollmentMySqlTestFixture.EnrollmentMigrationId);
        }
    }
}
