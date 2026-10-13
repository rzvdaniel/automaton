using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class NodeActivationService
{
    private readonly ApplicationDbContext db;
    private readonly INodeRecoveryEpochAuthority epochAuthority;
    private readonly INodeHostFenceReceiptValidator hostFenceValidator;
    private readonly NodeActivationTransactionHandler transactionHandler;

    public NodeActivationService(ApplicationDbContext db, TimeProvider timeProvider,
        INodeRecoveryEpochAuthority epochAuthority, INodeHostFenceReceiptValidator hostFenceValidator)
    {
        this.db = db;
        this.epochAuthority = epochAuthority;
        this.hostFenceValidator = hostFenceValidator;
        transactionHandler = new NodeActivationTransactionHandler(db, timeProvider, hostFenceValidator);
    }

    public async Task<NodeActivationResult> ActivateAsync(NodeIdentityResult identity, NodeActivationRequest request,
        byte[] canonicalRequest, CancellationToken cancellationToken)
    {
        if (!IsIdentityBound(identity, request))
            return NodeActivationResult.Conflict("activation_identity_mismatch");

        var digest = Convert.ToHexString(SHA256.HashData(canonicalRequest)).ToLowerInvariant();
        var enrollment = await FindCommittedEnrollmentAsync(identity, cancellationToken);
        if (enrollment is null)
            return NodeActivationResult.Conflict("activation_identity_revoked");

        var previous = await FindAttemptAsync(identity.NodeId, request.ActivationRequestId, cancellationToken);
        if (previous is not null)
            return ResolveRetry(previous, digest);

        if (!hostFenceValidator.IsConfigured ||
            !await epochAuthority.IsCurrentEpochAsync(request.ControlPlaneRealmId, request.RecoveryEpoch, cancellationToken))
            return NodeActivationResult.Unavailable;

        return await transactionHandler.CommitAsync(enrollment, identity, request, digest, cancellationToken);
    }

    private Task<NodeEnrollmentAuthorization?> FindCommittedEnrollmentAsync(NodeIdentityResult identity, CancellationToken cancellationToken) =>
        db.NodeEnrollmentAuthorizations.AsNoTracking().SingleOrDefaultAsync(value =>
            value.NodeId == identity.NodeId && value.InstallationId == identity.InstallationId &&
            value.ControlPlaneRealmId == identity.ControlPlaneRealmId && value.Status == NodeEnrollmentStatuses.Committed &&
            value.CertificateSha256 != null && value.NodeApiKeySha256 != null, cancellationToken);

    private Task<NodeActivationAttempt?> FindAttemptAsync(Guid nodeId, Guid requestId, CancellationToken cancellationToken) =>
        db.NodeActivationAttempts.AsNoTracking().SingleOrDefaultAsync(value =>
            value.NodeId == nodeId && value.ActivationRequestId == requestId, cancellationToken);

    private static bool IsIdentityBound(NodeIdentityResult identity, NodeActivationRequest request) =>
        identity.Accepted && request.NodeId == identity.NodeId && request.InstallationId == identity.InstallationId &&
        request.ControlPlaneRealmId == identity.ControlPlaneRealmId;

    private static NodeActivationResult ResolveRetry(NodeActivationAttempt previous, string digest) =>
        previous.RequestSha256 == digest
            ? NodeActivationResult.Accepted(previous.ImmutableReceiptJson)
            : NodeActivationResult.Conflict("activation_request_conflict");
}

internal sealed class NodeActivationTransactionHandler(
    ApplicationDbContext db,
    TimeProvider timeProvider,
    INodeHostFenceReceiptValidator hostFenceValidator)
{
    private static readonly JsonSerializerOptions ReceiptOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<NodeActivationResult> CommitAsync(NodeEnrollmentAuthorization enrollment, NodeIdentityResult identity,
        NodeActivationRequest request, string digest, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var management = await GetOrCreateManagementAsync(enrollment, request, cancellationToken);
            if (management is null || !IsManagementBound(management, request, enrollment))
                return await ResolveRequestRaceAsync(identity.NodeId, request.ActivationRequestId, digest, "activation_binding_conflict", cancellationToken);

            if (!TryPrepareTransition(management, request, timeProvider.GetUtcNow(), hostFenceValidator, out var transition))
                return await ResolveRequestRaceAsync(identity.NodeId, request.ActivationRequestId, digest, "host_fence_receipt_invalid", cancellationToken);

            if (request.ExpectedIncarnation == 0 && request.HostFenceReceipt is not null)
                return NodeActivationResult.Invalid;

            var changed = await ApplyCompareAndSwapAsync(identity, enrollment, request, transition!, cancellationToken);
            if (changed != 1)
                return await ResolveRequestRaceAsync(identity.NodeId, request.ActivationRequestId, digest, "incarnation_conflict", cancellationToken);

            await SaveAttemptAsync(db, identity, request, digest, transition!, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return NodeActivationResult.Accepted(transition!.ReceiptJson);
        }
        catch (OverflowException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NodeActivationResult.Conflict("incarnation_overflow");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ResolveRequestRaceAsync(identity.NodeId, request.ActivationRequestId, digest, "incarnation_conflict", cancellationToken);
        }
    }

    private async Task<NodeManagement?> GetOrCreateManagementAsync(NodeEnrollmentAuthorization enrollment,
        NodeActivationRequest request, CancellationToken cancellationToken)
    {
        var management = await db.NodeManagement.SingleOrDefaultAsync(value => value.NodeId == enrollment.NodeId, cancellationToken);
        if (management is not null || request.ExpectedIncarnation != 0)
            return management;
        management = new NodeManagement
        {
            NodeId = enrollment.NodeId,
            InstallationId = enrollment.InstallationId,
            EnrollmentAuthorizationId = enrollment.Id,
            ControlPlaneRealmId = enrollment.ControlPlaneRealmId,
            CredentialRevision = 1,
            RecoveryEpoch = request.RecoveryEpoch,
            CurrentIncarnation = null,
            Status = NodeManagementStatuses.Enrolled
        };
        db.NodeManagement.Add(management);
        await db.SaveChangesAsync(cancellationToken);
        return management;
    }

    private static bool IsManagementBound(NodeManagement management, NodeActivationRequest request,
        NodeEnrollmentAuthorization enrollment) =>
        management.InstallationId == request.InstallationId && management.EnrollmentAuthorizationId == enrollment.Id &&
        management.ControlPlaneRealmId == request.ControlPlaneRealmId && management.CredentialRevision == request.CredentialRevision &&
        management.RecoveryEpoch == request.RecoveryEpoch;

    private static bool TryPrepareTransition(NodeManagement management, NodeActivationRequest request,
        DateTimeOffset now, INodeHostFenceReceiptValidator validator, out NodeActivationTransition? transition)
    {
        transition = null;
        var initial = management.CurrentIncarnation is null;
        if (initial)
        {
            if (request.ExpectedIncarnation != 0 || management.Status != NodeManagementStatuses.Enrolled || request.HostFenceReceipt is not null)
                return false;
            transition = NodeActivationTransition.Initial;
            return true;
        }

        if (request.ExpectedIncarnation <= 0 || management.CurrentIncarnation != request.ExpectedIncarnation ||
            !IsFenceableStatus(management.Status) || request.HostFenceReceipt is null ||
            management.CurrentActivationRequestId is not Guid priorRequestId ||
            management.RecoveryCheckpointSha256 != request.RecoveryCheckpointSha256 ||
            !validator.TryValidate(request.HostFenceReceipt, request, management.CurrentIncarnation.Value,
                priorRequestId, management.RecoveryEpoch, management.HostFenceGeneration, now))
            return false;

        transition = NodeActivationTransition.Restart(checked(request.ExpectedIncarnation + 1),
            request.HostFenceReceipt.Payload.HostFenceGeneration, request.HostFenceReceipt.Payload.ReceiptId);
        return true;
    }

    private static bool IsFenceableStatus(string status) =>
        status is NodeManagementStatuses.Quiescing or NodeManagementStatuses.Fenced or NodeManagementStatuses.Recovering;

    private Task<int> ApplyCompareAndSwapAsync(NodeIdentityResult identity, NodeEnrollmentAuthorization enrollment,
        NodeActivationRequest request, NodeActivationTransition transition, CancellationToken cancellationToken) =>
        db.NodeManagement.Where(value => value.NodeId == identity.NodeId && value.InstallationId == request.InstallationId &&
                value.EnrollmentAuthorizationId == enrollment.Id && value.ControlPlaneRealmId == request.ControlPlaneRealmId &&
                value.CredentialRevision == request.CredentialRevision && value.RecoveryEpoch == request.RecoveryEpoch &&
                (transition.IsInitial
                    ? value.Status == NodeManagementStatuses.Enrolled && value.CurrentIncarnation == null
                    : value.CurrentIncarnation == request.ExpectedIncarnation &&
                      (value.Status == NodeManagementStatuses.Quiescing || value.Status == NodeManagementStatuses.Fenced ||
                       value.Status == NodeManagementStatuses.Recovering) &&
                      value.HostFenceGeneration < transition.HostFenceGeneration))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(value => value.CurrentIncarnation, transition.AssignedIncarnation)
                .SetProperty(value => value.Status, NodeManagementStatuses.Recovering)
                .SetProperty(value => value.CurrentActivationRequestId, request.ActivationRequestId)
                .SetProperty(value => value.RecoveryCheckpointSha256, request.RecoveryCheckpointSha256)
                .SetProperty(value => value.HostFenceGeneration, transition.HostFenceGeneration), cancellationToken);

    private static async Task SaveAttemptAsync(ApplicationDbContext db, NodeIdentityResult identity, NodeActivationRequest request,
        string digest, NodeActivationTransition transition, DateTime now, CancellationToken cancellationToken)
    {
        if (!transition.IsInitial)
        {
            db.NodeHostFenceReceiptUses.Add(new NodeHostFenceReceiptUse
            {
                ReceiptId = transition.HostFenceReceiptId!.Value,
                NodeId = identity.NodeId,
                ActivationRequestId = request.ActivationRequestId,
                HostFenceGeneration = transition.HostFenceGeneration,
                ConsumedAtUtc = now
            });
        }
        var receipt = new NodeActivationReceipt(1, identity.NodeId, identity.InstallationId,
            identity.ControlPlaneRealmId, request.ActivationRequestId, transition.AssignedIncarnation,
            request.RecoveryEpoch, request.RecoveryCheckpointSha256, NodeManagementStatuses.Recovering, now);
        var receiptJson = JsonSerializer.SerializeToUtf8Bytes(receipt, ReceiptOptions);
        transition.ReceiptJson = receiptJson;
        db.NodeActivationAttempts.Add(new NodeActivationAttempt
        {
            Id = Guid.NewGuid(), NodeId = identity.NodeId, InstallationId = identity.InstallationId,
            ActivationRequestId = request.ActivationRequestId, RequestSha256 = digest,
            ExpectedIncarnation = transition.IsInitial ? null : request.ExpectedIncarnation,
            AssignedIncarnation = transition.AssignedIncarnation, ControlPlaneRealmId = identity.ControlPlaneRealmId,
            RecoveryEpoch = request.RecoveryEpoch, CredentialRevision = request.CredentialRevision,
            Status = NodeManagementStatuses.Recovering, ImmutableReceiptJson = receiptJson,
            HostFenceReceiptId = transition.HostFenceReceiptId, CreatedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<NodeActivationResult> ResolveRequestRaceAsync(Guid nodeId, Guid requestId, string digest,
        string conflictCode, CancellationToken cancellationToken)
    {
        var winner = await db.NodeActivationAttempts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.NodeId == nodeId && value.ActivationRequestId == requestId, cancellationToken);
        return winner is not null && winner.RequestSha256 == digest
            ? NodeActivationResult.Accepted(winner.ImmutableReceiptJson)
            : NodeActivationResult.Conflict(conflictCode);
    }
}

internal sealed class NodeActivationTransition(long assignedIncarnation, bool isInitial, long hostFenceGeneration,
    Guid? hostFenceReceiptId)
{
    public static NodeActivationTransition Initial => new(1, true, 0, null);
    public static NodeActivationTransition Restart(long assignedIncarnation, long generation, Guid receiptId) =>
        new(assignedIncarnation, false, generation, receiptId);
    public long AssignedIncarnation { get; } = assignedIncarnation;
    public bool IsInitial { get; } = isInitial;
    public long HostFenceGeneration { get; } = hostFenceGeneration;
    public Guid? HostFenceReceiptId { get; } = hostFenceReceiptId;
    public byte[] ReceiptJson { get; set; } = Array.Empty<byte>();
}
