using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Security;
using Automaton.Api.Services;
using Automaton.Node.Enrollment;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

internal sealed class NodeEnrollmentTestHarness : IAsyncDisposable
{
    private static readonly Guid EnrollmentId = Guid.Parse("d3674f84-a2de-4e65-934a-9dc80c39deab");
    private static readonly Guid NodeId = Guid.Parse("a3c3bde1-2c8f-4b25-b154-16993bb1a44a");
    private static readonly Guid InstallationId = Guid.Parse("59c4d982-57ae-4f38-91a0-e176999d7b31");
    private static readonly Guid RealmId = Guid.Parse("cd9a7a11-085f-4f1b-a87b-a4732dc225f7");
    private const string EnrollmentToken = "synthetic-enrollment-token-only-for-tests";
    private readonly SqliteConnection connection;
    private readonly ApplicationDbContext db;
    private readonly NodeEnrollmentService service;
    private readonly NodeEnrollmentKeyMaterial keyMaterial;
    private readonly DateTimeOffset now;

    private NodeEnrollmentTestHarness(
        SqliteConnection connection,
        ApplicationDbContext db,
        NodeEnrollmentService service,
        NodeEnrollmentKeyMaterial keyMaterial,
        DateTimeOffset now)
    {
        this.connection = connection;
        this.db = db;
        this.service = service;
        this.keyMaterial = keyMaterial;
        this.now = now;
    }

    public static async Task<NodeEnrollmentTestHarness> CreateAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var material = NodeEnrollmentKeyMaterial.Create("CN=synthetic-node", now);
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var db = new ApplicationDbContext(options);
        await CreateLabSchemaAsync(connection);
        db.NodeEnrollmentAuthorizations.Add(CreatePendingEnrollment(material, now));
        await db.SaveChangesAsync();
        var service = new NodeEnrollmentService(
            db,
            new EphemeralDataProtectionProvider(NullLoggerFactory.Instance),
            new FrozenTimeProvider(now));
        return new NodeEnrollmentTestHarness(connection, db, service, material, now);
    }

    public async Task<BootstrapObservation> BootstrapAsync()
    {
        var result = await service.BootstrapAsync(EnrollmentId, ValidRequest(), CancellationToken.None);
        var stored = await db.NodeEnrollmentAuthorizations.AsNoTracking().SingleAsync(value => value.Id == EnrollmentId);
        var retry = await service.BootstrapAsync(EnrollmentId, ValidRequest(), CancellationToken.None);
        var identity = await service.AuthenticateManagementRequestAsync(
            result.Credentials?.NodeApiKey,
            CertificateHeader(),
            CancellationToken.None);
        var wrongKey = await service.AuthenticateManagementRequestAsync("node_wrong-key", CertificateHeader(), CancellationToken.None);
        return new BootstrapObservation(
            result.Accepted,
            result.Credentials is not null,
            stored.Status,
            stored.CertificateSha256 == Convert.ToHexString(SHA256.HashData(keyMaterial.CertificateDer)),
            stored.NodeApiKeySha256 == NodeEnrollmentCryptography.HashUtf8(result.Credentials?.NodeApiKey ?? string.Empty),
            stored.ProtectedCredentialBundle is { Length: > 0 },
            stored.CommittedAtUtc == now.UtcDateTime,
            retry.Accepted,
            identity.Accepted && identity.NodeId == NodeId,
            wrongKey.Accepted,
            result.Receipt);
    }

    public async Task<RecoveryObservation> RecoverAsync()
    {
        var bootstrap = await service.BootstrapAsync(EnrollmentId, ValidRequest(), CancellationToken.None);
        var challenge = await service.CreateRecoveryChallengeAsync(EnrollmentId, InstallationId, RealmId, CancellationToken.None);
        if (!challenge.Accepted)
            return new RecoveryObservation(false, false, false, false, false);

        var message = NodeEnrollmentCryptography.BuildRecoveryMessage(
            challenge.ChallengeId,
            DecodeBase64Url(challenge.NonceBase64Url!),
            EnrollmentId,
            InstallationId,
            RealmId);
        var signature = keyMaterial.SignRecoveryChallenge(message);
        var request = new NodeRecoveryCompletionRequest(
            challenge.ChallengeId, InstallationId, RealmId, Convert.ToBase64String(signature));
        var recovered = await service.CompleteRecoveryAsync(EnrollmentId, request, CancellationToken.None);
        var replay = await service.CompleteRecoveryAsync(EnrollmentId, request, CancellationToken.None);
        var storedChallenge = await db.NodeEnrollmentRecoveryChallenges.AsNoTracking().SingleAsync(value => value.Id == challenge.ChallengeId);
        return new RecoveryObservation(
            true,
            recovered.Accepted && recovered.Receipt == bootstrap.Receipt,
            recovered.Credentials == bootstrap.Credentials,
            replay.Accepted,
            storedChallenge.ConsumedAtUtc == now.UtcDateTime);
    }

    public async Task<FailedBootstrapObservation> TryForeignKeyAsync()
    {
        using var foreignMaterial = NodeEnrollmentKeyMaterial.Create("CN=foreign-node", now);
        var request = new NodeBootstrapRequest(
            EnrollmentToken,
            Convert.ToBase64String(foreignMaterial.CsrDer),
            Convert.ToBase64String(foreignMaterial.CertificateDer));
        var result = await service.BootstrapAsync(EnrollmentId, request, CancellationToken.None);
        var stored = await db.NodeEnrollmentAuthorizations.SingleAsync(value => value.Id == EnrollmentId);
        return new FailedBootstrapObservation(
            result.Accepted,
            stored.Status,
            stored.CertificateDer is null && stored.CertificateSha256 is null &&
            stored.NodeApiKeySha256 is null && stored.ProtectedCredentialBundle is null);
    }

    public async ValueTask DisposeAsync()
    {
        keyMaterial.Dispose();
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private static NodeEnrollmentAuthorization CreatePendingEnrollment(NodeEnrollmentKeyMaterial material, DateTimeOffset current) => new()
    {
        Id = EnrollmentId,
        NodeId = NodeId,
        InstallationId = InstallationId,
        ControlPlaneRealmId = RealmId,
        OwnerScope = "synthetic/test-scope",
        PinnedPublicKeySha256 = material.PinnedPublicKeySha256,
        EnrollmentTokenSha256 = NodeEnrollmentCryptography.HashUtf8(EnrollmentToken),
        Status = NodeEnrollmentStatuses.Pending,
        ExpiresAtUtc = current.AddHours(1).UtcDateTime
    };

    private static async Task CreateLabSchemaAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE NodeEnrollmentAuthorizations (
                Id TEXT NOT NULL PRIMARY KEY,
                NodeId TEXT NOT NULL,
                InstallationId TEXT NOT NULL,
                ControlPlaneRealmId TEXT NOT NULL,
                OwnerScope TEXT NOT NULL,
                PinnedPublicKeySha256 TEXT NOT NULL,
                EnrollmentTokenSha256 TEXT NOT NULL,
                Status TEXT NOT NULL,
                ExpiresAtUtc TEXT NOT NULL,
                CommittedAtUtc TEXT NULL,
                CertificateDer BLOB NULL,
                CertificateSha256 TEXT NULL,
                NodeApiKeySha256 TEXT NULL,
                ProtectedCredentialBundle BLOB NULL,
                CredentialBundleExpiresAtUtc TEXT NULL
            );
            CREATE TABLE NodeEnrollmentRecoveryChallenges (
                Id TEXT NOT NULL PRIMARY KEY,
                EnrollmentAuthorizationId TEXT NOT NULL,
                Nonce BLOB NOT NULL,
                PublicKeySha256 TEXT NOT NULL,
                ExpiresAtUtc TEXT NOT NULL,
                ConsumedAtUtc TEXT NULL,
                FOREIGN KEY (EnrollmentAuthorizationId) REFERENCES NodeEnrollmentAuthorizations(Id)
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private NodeBootstrapRequest ValidRequest() => new(
        EnrollmentToken,
        Convert.ToBase64String(keyMaterial.CsrDer),
        Convert.ToBase64String(keyMaterial.CertificateDer));

    private HeaderDictionary CertificateHeader() => new()
    {
        ["X-Forwarded-Tls-Client-Cert"] = Convert.ToBase64String(keyMaterial.CertificateDer)
    };

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

internal sealed record BootstrapObservation(
    bool Accepted,
    bool HasCredentials,
    string Status,
    bool CertificatePinned,
    bool ApiKeyHashStored,
    bool CredentialEnvelopeProtected,
    bool CommitTimestampStored,
    bool ReplayAccepted,
    bool CorrectIdentityAccepted,
    bool WrongApiKeyAccepted,
    NodeEnrollmentReceipt? Receipt);

internal sealed record RecoveryObservation(
    bool ChallengeCreated,
    bool OriginalReceiptReturned,
    bool OriginalCredentialsReturned,
    bool ReplayAccepted,
    bool ChallengeConsumed);

internal sealed record FailedBootstrapObservation(bool Accepted, string Status, bool HasNoPartialCredentialState);
