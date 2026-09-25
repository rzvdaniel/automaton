using System.Data;
using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>
/// Issues one-time enrollment credentials and secret grants for Hosted Runners.
/// </summary>
public sealed class HostedRunnerEnrollmentProvisioningService(
    ApplicationDbContext db,
    ApiKeyService apiKeyService,
    IAuditEventService auditEventService,
    IOptions<RunnerEnrollmentOptions> options,
    ISecretStore secretStore,
    IExecutionSecretGrantService secretGrantService)
{
    public async Task<HostedRunnerEnrollmentMaterial> CreateAsync(
        Guid hostedRunnerId,
        Guid operationId,
        string actionKey,
        Guid infrastructureRunnerId,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational() && db.Database.CurrentTransaction == null)
            transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var hostedRunner = await db.HostedRunners
                .FirstOrDefaultAsync(x => x.Id == hostedRunnerId && x.DeletedAt == null, cancellationToken)
                ?? throw new KeyNotFoundException("The hosted Runner was not found.");
            var runnerHostId = hostedRunner.RunnerHostId
                ?? throw new InvalidOperationException("The hosted Runner has no assigned host.");
            var host = await db.RunnerHosts.FirstOrDefaultAsync(x => x.Id == runnerHostId, cancellationToken)
                ?? throw new InvalidOperationException("The hosted Runner host was not found.");
            if (host.InfrastructureRunnerId != infrastructureRunnerId)
                throw new UnauthorizedAccessException("The enrollment target does not match the host's Infrastructure Runner.");

            var now = DateTime.UtcNow;
            var expiresAtUtc = now.AddMinutes(Math.Clamp(options.Value.TokenLifetimeMinutes, 1, 60));
            await RevokePriorEnrollmentsAsync(hostedRunnerId, now, cancellationToken);
            await DeactivateExistingApiKeyAsync(hostedRunner, cancellationToken);

            var material = await CreateEnrollmentAndGrantAsync(
                hostedRunner, runnerHostId, operationId, actionKey, infrastructureRunnerId,
                now, expiresAtUtc, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await WriteCreatedAuditAsync(hostedRunner, operationId, material, cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return material;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<HostedRunnerEnrollmentMaterial> CreateEnrollmentAndGrantAsync(
        HostedRunner hostedRunner,
        Guid runnerHostId,
        Guid operationId,
        string actionKey,
        Guid infrastructureRunnerId,
        DateTime now,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var token = GenerateEnrollmentToken();
        var enrollment = new RunnerEnrollment
        {
            Id = Guid.NewGuid(),
            RunnerHostId = runnerHostId,
            HostedRunnerId = hostedRunner.Id,
            OperationId = operationId,
            RequestedByUserId = hostedRunner.UserId,
            ExpectedPurpose = RunnerPurposes.Managed,
            ExpectedCapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed), RunnerPurposes.Managed),
            EnrollmentTokenHash = HashToken(token),
            InstallationId = $"hr_{hostedRunner.Id:N}",
            Status = RunnerEnrollmentStatuses.Pending,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        };
        db.RunnerEnrollments.Add(enrollment);
        var enrollmentVersion = await secretStore.CreateVersionAsync(
            new SecretOwner(SecretOwnerTypes.HostedRunner, hostedRunner.Id),
            "hosted-runner-enrollment", token, expiresAtUtc, cancellationToken);
        enrollment.SecretVersionId = enrollmentVersion.SecretVersionId;
        var grantId = await secretGrantService.CreateGrantAsync(
            enrollmentVersion.SecretVersionId,
            new SecretGrantScope(operationId, InfrastructureAppTrust.PlatformAppId, actionKey,
                infrastructureRunnerId, runnerHostId, SecretOwnerTypes.HostedRunner, hostedRunner.Id),
            "AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN", expiresAtUtc, cancellationToken,
            SecretDeliveryPolicies.SingleDelivery);
        return new(enrollment.Id, grantId, expiresAtUtc);
    }

    private Task WriteCreatedAuditAsync(
        HostedRunner hostedRunner,
        Guid operationId,
        HostedRunnerEnrollmentMaterial material,
        CancellationToken cancellationToken)
        => auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.RunnerEnrollment,
            EntityId = material.EnrollmentId,
            EventType = AuditEventTypes.RunnerEnrollmentCreated,
            Message = $"One-time enrollment created for hosted Runner '{hostedRunner.Name}'.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = hostedRunner.UserId,
            TargetUserId = hostedRunner.UserId,
            Metadata = new { hostedRunner.Id, OperationId = operationId, GrantId = material.SecretGrantId, ExpiresAtUtc = material.ExpiresAtUtc }
        }, cancellationToken);

    private async Task RevokePriorEnrollmentsAsync(
        Guid hostedRunnerId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken)
    {
        var priorEnrollments = await db.RunnerEnrollments
            .Where(x => x.HostedRunnerId == hostedRunnerId && x.Status == RunnerEnrollmentStatuses.Pending)
            .ToListAsync(cancellationToken);
        var priorVersionIds = priorEnrollments
            .Where(x => x.SecretVersionId.HasValue)
            .Select(x => x.SecretVersionId!.Value)
            .ToArray();
        foreach (var prior in priorEnrollments)
        {
            prior.Status = RunnerEnrollmentStatuses.Revoked;
            prior.FailureReason = "Superseded by a new hosted Runner enrollment.";
        }

        if (priorVersionIds.Length == 0)
            return;
        var priorVersions = await db.SecretVersions.Where(x => priorVersionIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var priorGrants = await db.ExecutionSecretGrants.Where(x => priorVersionIds.Contains(x.SecretVersionId)).ToListAsync(cancellationToken);
        foreach (var priorVersion in priorVersions)
            priorVersion.RevokedAtUtc ??= revokedAtUtc;
        foreach (var grant in priorGrants)
            grant.RevokedAtUtc ??= revokedAtUtc;
    }

    private async Task DeactivateExistingApiKeyAsync(HostedRunner hostedRunner, CancellationToken cancellationToken)
    {
        if (!hostedRunner.ApiKeyId.HasValue)
            return;
        var oldKey = await db.ApiKeys.FirstOrDefaultAsync(x => x.Id == hostedRunner.ApiKeyId.Value, cancellationToken);
        if (oldKey is not null)
            oldKey.IsActive = false;
        hostedRunner.ApiKeyId = null;
    }

    private static string GenerateEnrollmentToken()
        => $"re_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}"
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
