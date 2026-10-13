using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class NodeActivationReadbackService(
    ApplicationDbContext db,
    INodeRecoveryEpochAuthority epochAuthority)
{
    private static readonly JsonSerializerOptions ReceiptOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<NodeActivationReadbackResult> ReadCurrentAsync(
        NodeIdentityResult identity,
        CancellationToken cancellationToken)
    {
        if (!identity.Accepted)
            return NodeActivationReadbackResult.Unauthorized;

        var authorityBefore = await epochAuthority.ReadCurrentAsync(identity.ControlPlaneRealmId, cancellationToken);
        if (!IsValidAuthority(authorityBefore, identity.ControlPlaneRealmId))
            return NodeActivationReadbackResult.Unavailable;

        NodeActivationCurrentReadback? snapshot;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken))
        {
            var enrollment = await db.NodeEnrollmentAuthorizations.AsNoTracking().SingleOrDefaultAsync(value =>
                value.NodeId == identity.NodeId && value.InstallationId == identity.InstallationId &&
                value.ControlPlaneRealmId == identity.ControlPlaneRealmId &&
                value.Status == NodeEnrollmentStatuses.Committed && value.CertificateSha256 != null &&
                value.NodeApiKeySha256 != null, cancellationToken);
            if (enrollment is null)
                return NodeActivationReadbackResult.Unauthorized;

            var management = await db.NodeManagement.AsNoTracking()
                .SingleOrDefaultAsync(value => value.NodeId == identity.NodeId, cancellationToken);
            var hasAttempts = await db.NodeActivationAttempts.AsNoTracking()
                .AnyAsync(value => value.NodeId == identity.NodeId, cancellationToken);
            if (management is null)
            {
                if (hasAttempts)
                    return NodeActivationReadbackResult.Conflict;
                snapshot = CreateNoActivation(identity, authorityBefore!);
            }
            else
            {
                if (!IsManagementBound(management, identity, enrollment))
                    return NodeActivationReadbackResult.Conflict;
                snapshot = await CreateManagementReadbackAsync(identity, management, authorityBefore!, cancellationToken);
                if (snapshot is null)
                    return NodeActivationReadbackResult.Conflict;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var authorityAfter = await epochAuthority.ReadCurrentAsync(identity.ControlPlaneRealmId, cancellationToken);
        if (!IsValidAuthority(authorityAfter, identity.ControlPlaneRealmId) ||
            !SameAuthority(authorityBefore!, authorityAfter!))
            return NodeActivationReadbackResult.Unavailable;

        return NodeActivationReadbackResult.Success(snapshot!);
    }

    private async Task<NodeActivationCurrentReadback?> CreateManagementReadbackAsync(
        NodeIdentityResult identity,
        NodeManagement management,
        NodeRecoveryAuthoritySnapshot authority,
        CancellationToken cancellationToken)
    {
        if (management.CurrentIncarnation is null)
            return await CreateEnrolledReadbackAsync(identity, management, authority, cancellationToken);

        if (management.CurrentIncarnation <= 0 || management.RecoveryEpoch <= 0 ||
            management.CurrentActivationRequestId is not Guid requestId ||
            management.Status is not (NodeManagementStatuses.Recovering or NodeManagementStatuses.Quiescing or NodeManagementStatuses.Fenced))
            return null;

        return await CreateAllocatedReadbackAsync(identity, management, authority, requestId, cancellationToken);
    }

    private async Task<NodeActivationCurrentReadback?> CreateEnrolledReadbackAsync(NodeIdentityResult identity,
        NodeManagement management, NodeRecoveryAuthoritySnapshot authority, CancellationToken cancellationToken)
    {
        if (management.Status != NodeManagementStatuses.Enrolled || management.CurrentActivationRequestId is not null ||
            await db.NodeActivationAttempts.AsNoTracking().AnyAsync(value => value.NodeId == identity.NodeId, cancellationToken) ||
            management.RecoveryEpoch <= 0 || management.RecoveryEpoch != authority.RecoveryEpoch)
            return null;
        return CreateReadback(identity, management.CredentialRevision, management.RecoveryEpoch,
            authority.RecoveryEpoch, NodeManagementStatuses.Enrolled, null, null, null, null);
    }

    private async Task<NodeActivationCurrentReadback?> CreateAllocatedReadbackAsync(NodeIdentityResult identity,
        NodeManagement management, NodeRecoveryAuthoritySnapshot authority, Guid requestId, CancellationToken cancellationToken)
    {
        var attempt = await db.NodeActivationAttempts.AsNoTracking().SingleOrDefaultAsync(value =>
            value.NodeId == identity.NodeId && value.ActivationRequestId == requestId, cancellationToken);
        if (attempt is null || !IsCurrentAttemptBound(attempt, management, identity) ||
            !TryValidateReceipt(attempt, out var receipt))
            return null;

        var effectiveStatus = management.RecoveryEpoch == authority.RecoveryEpoch
            ? management.Status
            : NodeManagementStatuses.Fenced;
        return CreateReadback(identity, management.CredentialRevision, management.RecoveryEpoch,
            authority.RecoveryEpoch, effectiveStatus, management.CurrentIncarnation.Value, requestId,
            attempt.RequestSha256, attempt.ImmutableReceiptJson);
    }

    private static NodeActivationCurrentReadback CreateNoActivation(
        NodeIdentityResult identity,
        NodeRecoveryAuthoritySnapshot authority) =>
        CreateReadback(identity, 1, null, authority.RecoveryEpoch, "NoActivation", null, null, null, null);

    private static NodeActivationCurrentReadback CreateReadback(
        NodeIdentityResult identity,
        long credentialRevision,
        long? recoveryEpoch,
        long authoritativeRecoveryEpoch,
        string status,
        long? incarnation,
        Guid? requestId,
        string? requestSha256,
        byte[]? receipt) => new(
            1,
            identity.ControlPlaneRealmId,
            identity.NodeId,
            identity.InstallationId,
            credentialRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            recoveryEpoch?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            authoritativeRecoveryEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            status,
            incarnation?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            requestId,
            requestSha256,
            receipt is null ? null : Convert.ToBase64String(receipt));

    private static bool IsManagementBound(NodeManagement management, NodeIdentityResult identity,
        NodeEnrollmentAuthorization enrollment) =>
        management.NodeId == identity.NodeId && management.InstallationId == identity.InstallationId &&
        management.EnrollmentAuthorizationId == enrollment.Id &&
        management.ControlPlaneRealmId == identity.ControlPlaneRealmId && management.CredentialRevision > 0;

    private static bool IsCurrentAttemptBound(NodeActivationAttempt attempt, NodeManagement management,
        NodeIdentityResult identity) =>
        attempt.NodeId == identity.NodeId && attempt.InstallationId == identity.InstallationId &&
        attempt.ControlPlaneRealmId == identity.ControlPlaneRealmId &&
        attempt.CredentialRevision == management.CredentialRevision &&
        attempt.RecoveryEpoch == management.RecoveryEpoch &&
        attempt.AssignedIncarnation == management.CurrentIncarnation &&
        attempt.Status == NodeManagementStatuses.Recovering &&
        attempt.RequestSha256.Length == 64 && attempt.RequestSha256.All(IsLowerHex);

    private static bool TryValidateReceipt(NodeActivationAttempt attempt, out NodeActivationReceipt? receipt)
    {
        receipt = null;
        try
        {
            receipt = JsonSerializer.Deserialize<NodeActivationReceipt>(attempt.ImmutableReceiptJson, ReceiptOptions);
            if (receipt is null || receipt.SchemaVersion != 1 || receipt.NodeId != attempt.NodeId ||
                receipt.InstallationId != attempt.InstallationId || receipt.ControlPlaneRealmId != attempt.ControlPlaneRealmId ||
                receipt.ActivationRequestId != attempt.ActivationRequestId || receipt.Incarnation != attempt.AssignedIncarnation ||
                receipt.RecoveryEpoch != attempt.RecoveryEpoch || receipt.Status != NodeManagementStatuses.Recovering ||
                receipt.CreatedAtUtc == default || receipt.CreatedAtUtc.Kind != DateTimeKind.Utc ||
                !NodeActivationRequestParser.IsDigest(receipt.RecoveryCheckpointSha256))
                return false;

            var canonical = JsonSerializer.SerializeToUtf8Bytes(receipt, ReceiptOptions);
            return CryptographicOperations.FixedTimeEquals(canonical, attempt.ImmutableReceiptJson);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidAuthority(NodeRecoveryAuthoritySnapshot? authority, Guid realmId) =>
        authority is not null && authority.ControlPlaneRealmId == realmId && authority.RecoveryEpoch > 0 &&
        !string.IsNullOrWhiteSpace(authority.AuthorityRevision) && authority.AuthorityRevision.Length <= 128 &&
        !authority.AuthorityRevision.Any(char.IsControl) && NodeActivationRequestParser.IsDigest(authority.AttestationSha256);

    private static bool SameAuthority(NodeRecoveryAuthoritySnapshot left, NodeRecoveryAuthoritySnapshot right) =>
        left.ControlPlaneRealmId == right.ControlPlaneRealmId && left.RecoveryEpoch == right.RecoveryEpoch &&
        string.Equals(left.AuthorityRevision, right.AuthorityRevision, StringComparison.Ordinal) &&
        string.Equals(left.AttestationSha256, right.AttestationSha256, StringComparison.Ordinal);

    private static bool IsLowerHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f';
}

public sealed record NodeActivationCurrentReadback(
    int SchemaVersion,
    Guid ControlPlaneRealmId,
    Guid NodeId,
    Guid InstallationId,
    string CredentialRevision,
    string? RecoveryEpoch,
    string AuthoritativeRecoveryEpoch,
    string Status,
    string? CurrentIncarnation,
    Guid? CurrentActivationRequestId,
    string? RequestSha256,
    string? ActivationReceiptJsonBase64);

public enum NodeActivationReadbackDisposition { Success, Conflict, Unauthorized, Unavailable }

public sealed record NodeActivationReadbackResult(
    NodeActivationReadbackDisposition Disposition,
    NodeActivationCurrentReadback? Readback)
{
    public static NodeActivationReadbackResult Success(NodeActivationCurrentReadback readback) =>
        new(NodeActivationReadbackDisposition.Success, readback);
    public static NodeActivationReadbackResult Conflict => new(NodeActivationReadbackDisposition.Conflict, null);
    public static NodeActivationReadbackResult Unauthorized => new(NodeActivationReadbackDisposition.Unauthorized, null);
    public static NodeActivationReadbackResult Unavailable => new(NodeActivationReadbackDisposition.Unavailable, null);
}
