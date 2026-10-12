using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

internal sealed class NodeEnrollmentBootstrapHandler(
    ApplicationDbContext db,
    IDataProtectionProvider protectionProvider,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan CredentialLifetime = TimeSpan.FromHours(24);
    private readonly IDataProtector bundleProtector = protectionProvider.CreateProtector("Automaton.Node.EnrollmentCredentials.v1");

    public async Task<NodeEnrollmentOperationResult> BootstrapAsync(Guid enrollmentId, NodeBootstrapRequest request, CancellationToken cancellationToken)
    {
        if (!TryDecodeBoundedBase64(request.CsrDerBase64, out var csrDer) ||
            !TryDecodeBoundedBase64(request.CertificateDerBase64, out var certificateDer) ||
            string.IsNullOrEmpty(request.EnrollmentToken) || request.EnrollmentToken.Length > 256)
        {
            return NodeEnrollmentOperationResult.Invalid;
        }

        var record = await db.NodeEnrollmentAuthorizations.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == enrollmentId, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (!IsPendingAuthorization(record, request.EnrollmentToken, now))
        {
            return NodeEnrollmentOperationResult.Rejected;
        }

        if (!NodeEnrollmentCryptography.ValidateEnrollmentProof(
                csrDer, certificateDer, record!.PinnedPublicKeySha256, now, out var certificate) || certificate is null)
        {
            return NodeEnrollmentOperationResult.Rejected;
        }

        using (certificate)
        {
            var material = NodeEnrollmentBootstrapMaterial.Create(record, certificateDer, now, bundleProtector);
            return await CommitAsync(enrollmentId, record, material, now, cancellationToken);
        }
    }

    private async Task<NodeEnrollmentOperationResult> CommitAsync(
        Guid enrollmentId,
        NodeEnrollmentAuthorization expected,
        NodeEnrollmentBootstrapMaterial material,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var changed = await db.NodeEnrollmentAuthorizations
            .Where(value => value.Id == enrollmentId && value.Status == NodeEnrollmentStatuses.Pending &&
                            value.ExpiresAtUtc > now &&
                            value.EnrollmentTokenSha256 == expected.EnrollmentTokenSha256 &&
                            value.PinnedPublicKeySha256 == expected.PinnedPublicKeySha256)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(value => value.Status, NodeEnrollmentStatuses.Committed)
                .SetProperty(value => value.CommittedAtUtc, material.Receipt.CommittedAtUtc)
                .SetProperty(value => value.CertificateDer, material.CertificateDer)
                .SetProperty(value => value.CertificateSha256, material.Receipt.CertificateSha256)
                .SetProperty(value => value.NodeApiKeySha256, NodeEnrollmentCryptography.HashUtf8(material.Credentials.NodeApiKey))
                .SetProperty(value => value.ProtectedCredentialBundle, material.ProtectedBundle)
                .SetProperty(value => value.CredentialBundleExpiresAtUtc, material.CredentialBundleExpiresAtUtc),
                cancellationToken);
        if (changed != 1)
            return NodeEnrollmentOperationResult.Rejected;

        await transaction.CommitAsync(cancellationToken);
        return NodeEnrollmentOperationResult.Success(material.Receipt, material.Credentials);
    }

    private static bool IsPendingAuthorization(NodeEnrollmentAuthorization? record, string token, DateTime now) =>
        record is not null && record.Status == NodeEnrollmentStatuses.Pending && record.ExpiresAtUtc > now &&
        NodeEnrollmentCryptography.MatchesSha256Hex(
            NodeEnrollmentCryptography.HashUtf8(token), record.EnrollmentTokenSha256);

    internal static bool TryDecodeBoundedBase64(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrEmpty(value) || value.Length > 24 * 1024 || value.Any(char.IsWhiteSpace))
            return false;
        try
        {
            bytes = Convert.FromBase64String(value);
            return Convert.ToBase64String(bytes) == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal sealed record NodeEnrollmentBootstrapMaterial(
    byte[] CertificateDer,
    NodeEnrollmentReceipt Receipt,
    NodeEnrollmentCredentialBundle Credentials,
    byte[] ProtectedBundle,
    DateTime CredentialBundleExpiresAtUtc)
{
    public static NodeEnrollmentBootstrapMaterial Create(
        NodeEnrollmentAuthorization record,
        byte[] certificateDer,
        DateTime now,
        IDataProtector protector)
    {
        var receipt = new NodeEnrollmentReceipt(
            record.Id,
            record.NodeId,
            record.InstallationId,
            record.ControlPlaneRealmId,
            Convert.ToHexString(SHA256.HashData(certificateDer)),
            now);
        var apiKey = "node_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var credentials = new NodeEnrollmentCredentialBundle(receipt, apiKey);
        var protectedBundle = Encoding.UTF8.GetBytes(protector.Protect(JsonSerializer.Serialize(credentials)));
        return new NodeEnrollmentBootstrapMaterial(
            certificateDer,
            receipt,
            credentials,
            protectedBundle,
            now.Add(TimeSpan.FromHours(24)));
    }
}

internal sealed class NodeEnrollmentRecoveryHandler(
    ApplicationDbContext db,
    IDataProtectionProvider protectionProvider,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private readonly IDataProtector bundleProtector = protectionProvider.CreateProtector("Automaton.Node.EnrollmentCredentials.v1");

    public async Task<NodeEnrollmentChallengeResult> CreateChallengeAsync(Guid enrollmentId, Guid installationId, Guid realmId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var exists = await db.NodeEnrollmentAuthorizations.AsNoTracking().AnyAsync(value =>
            value.Id == enrollmentId && value.Status == NodeEnrollmentStatuses.Committed &&
            value.InstallationId == installationId && value.ControlPlaneRealmId == realmId &&
            value.CertificateSha256 != null, cancellationToken);
        if (!exists)
            return NodeEnrollmentChallengeResult.Rejected;

        var challenge = new NodeEnrollmentRecoveryChallenge
        {
            Id = Guid.NewGuid(),
            EnrollmentAuthorizationId = enrollmentId,
            Nonce = RandomNumberGenerator.GetBytes(32),
            PublicKeySha256 = await db.NodeEnrollmentAuthorizations.AsNoTracking()
                .Where(value => value.Id == enrollmentId)
                .Select(value => value.PinnedPublicKeySha256)
                .SingleAsync(cancellationToken),
            ExpiresAtUtc = now.Add(ChallengeLifetime)
        };
        db.NodeEnrollmentRecoveryChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);
        return NodeEnrollmentChallengeResult.Success(challenge.Id, challenge.Nonce, challenge.ExpiresAtUtc);
    }

    public async Task<NodeEnrollmentOperationResult> CompleteAsync(Guid enrollmentId, NodeRecoveryCompletionRequest request, CancellationToken cancellationToken)
    {
        if (!NodeEnrollmentBootstrapHandler.TryDecodeBoundedBase64(request.SignatureBase64, out var signature))
            return NodeEnrollmentOperationResult.Invalid;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var proof = await LoadProofAsync(enrollmentId, request, now, cancellationToken);
        if (proof is null || !Verify(proof, request, signature))
            return NodeEnrollmentOperationResult.Rejected;
        if (!await ConsumeAsync(proof.Challenge, proof.Record.PinnedPublicKeySha256, now, cancellationToken))
            return NodeEnrollmentOperationResult.Rejected;

        var bundle = UnprotectBundle(proof.Record, now);
        await transaction.CommitAsync(cancellationToken);
        return NodeEnrollmentOperationResult.Success(CreateReceipt(proof.Record, now), bundle);
    }

    private Task<NodeRecoveryProof?> LoadProofAsync(Guid enrollmentId, NodeRecoveryCompletionRequest request, DateTime now, CancellationToken cancellationToken) =>
        (from challenge in db.NodeEnrollmentRecoveryChallenges.AsNoTracking()
         join record in db.NodeEnrollmentAuthorizations.AsNoTracking() on challenge.EnrollmentAuthorizationId equals record.Id
         where challenge.Id == request.ChallengeId && challenge.EnrollmentAuthorizationId == enrollmentId &&
               challenge.ConsumedAtUtc == null && challenge.ExpiresAtUtc > now &&
               record.Status == NodeEnrollmentStatuses.Committed && record.InstallationId == request.InstallationId &&
               record.ControlPlaneRealmId == request.ControlPlaneRealmId
         select new NodeRecoveryProof(challenge, record)).SingleOrDefaultAsync(cancellationToken);

    private static bool Verify(NodeRecoveryProof proof, NodeRecoveryCompletionRequest request, byte[] signature) =>
        proof.Record.CertificateDer is not null && proof.Record.CertificateSha256 is not null &&
        proof.Challenge.PublicKeySha256 == proof.Record.PinnedPublicKeySha256 &&
        NodeEnrollmentCryptography.VerifyRecoveryProof(
            proof.Record.CertificateDer,
            NodeEnrollmentCryptography.BuildRecoveryMessage(
                proof.Challenge.Id,
                proof.Challenge.Nonce,
                proof.Record.Id,
                request.InstallationId,
                request.ControlPlaneRealmId),
            signature);

    private async Task<bool> ConsumeAsync(
        NodeEnrollmentRecoveryChallenge challenge,
        string expectedPublicKeySha256,
        DateTime now,
        CancellationToken cancellationToken) =>
        await db.NodeEnrollmentRecoveryChallenges
            .Where(value => value.Id == challenge.Id && value.EnrollmentAuthorizationId == challenge.EnrollmentAuthorizationId &&
                            value.ConsumedAtUtc == null && value.ExpiresAtUtc > now &&
                            value.PublicKeySha256 == expectedPublicKeySha256)
            .ExecuteUpdateAsync(updates => updates.SetProperty(value => value.ConsumedAtUtc, now), cancellationToken) == 1;

    private NodeEnrollmentCredentialBundle? UnprotectBundle(NodeEnrollmentAuthorization record, DateTime now)
    {
        if (record.ProtectedCredentialBundle is null || record.CredentialBundleExpiresAtUtc <= now)
            return null;
        var protectedText = Encoding.UTF8.GetString(record.ProtectedCredentialBundle);
        return JsonSerializer.Deserialize<NodeEnrollmentCredentialBundle>(bundleProtector.Unprotect(protectedText));
    }

    private static NodeEnrollmentReceipt CreateReceipt(NodeEnrollmentAuthorization record, DateTime now) =>
        new(record.Id, record.NodeId, record.InstallationId, record.ControlPlaneRealmId,
            record.CertificateSha256!, record.CommittedAtUtc ?? now);
}

internal sealed record NodeRecoveryProof(NodeEnrollmentRecoveryChallenge Challenge, NodeEnrollmentAuthorization Record);

internal sealed class NodeManagementAuthenticationHandler(
    ApplicationDbContext db,
    TimeProvider timeProvider,
    NodeProxyCertificateValidator proxyCertificateValidator)
{
    public async Task<NodeIdentityResult> AuthenticateAsync(
        Microsoft.AspNetCore.Http.HttpContext context,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length > 256)
            return NodeIdentityResult.Rejected;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var proxyCertificate = await context.Connection.GetClientCertificateAsync(cancellationToken);
        if (!proxyCertificateValidator.IsTrustedPeer(proxyCertificate, now) ||
            !NodeCertificateHeaderDecoder.TryDecode(context.Request.Headers, out var forwardedCertificate))
            return NodeIdentityResult.Rejected;

        using (forwardedCertificate!.Certificate)
        {
            if (forwardedCertificate.Certificate.NotBefore.ToUniversalTime() > now ||
                forwardedCertificate.Certificate.NotAfter.ToUniversalTime() <= now)
                return NodeIdentityResult.Rejected;

            var certificateSha256 = Convert.ToHexString(forwardedCertificate.Sha256DerFingerprint);
            var apiKeySha256 = NodeEnrollmentCryptography.HashUtf8(apiKey);
            var identity = await db.NodeEnrollmentAuthorizations.AsNoTracking()
                .Where(value => value.Status == NodeEnrollmentStatuses.Committed &&
                                value.CertificateSha256 == certificateSha256 && value.NodeApiKeySha256 == apiKeySha256)
                .Select(value => new NodeIdentityResult(true, value.NodeId, value.InstallationId, value.ControlPlaneRealmId))
                .SingleOrDefaultAsync(cancellationToken);
            return identity ?? NodeIdentityResult.Rejected;
        }
    }
}
