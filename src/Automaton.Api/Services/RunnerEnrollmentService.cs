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

public sealed class RunnerEnrollmentService(
    ApplicationDbContext db,
    ApiKeyService apiKeyService,
    IAuditEventService auditEventService,
    IOptions<RunnerEnrollmentOptions> options,
    IOptions<HostedRunnerOptions> hostedRunnerOptions,
    ISecretStore secretStore,
    IExecutionSecretGrantService secretGrantService,
    ILogger<RunnerEnrollmentService> logger)
{
    public async Task<RunnerEnrollmentCreatedResponse> CreateAsync(
        Guid runnerHostId,
        Guid requestedByUserId,
        CancellationToken cancellationToken)
    {
        var host = await db.RunnerHosts
            .FirstOrDefaultAsync(x => x.Id == runnerHostId, cancellationToken);

        if (host == null)
        {
            throw new KeyNotFoundException("Runner host was not found.");
        }

        if (host.InfrastructureRunnerId.HasValue)
        {
            throw new InvalidOperationException("The Runner host already has an Infrastructure Runner.");
        }

        var pending = await db.RunnerEnrollments
            .Where(x => x.RunnerHostId == runnerHostId && x.Status == RunnerEnrollmentStatuses.Pending)
            .ToListAsync(cancellationToken);

        foreach (var existing in pending)
        {
            existing.Status = RunnerEnrollmentStatuses.Revoked;
            existing.FailureReason = "Superseded by a new enrollment.";
        }

        var now = DateTime.UtcNow;
        var lifetimeMinutes = Math.Clamp(options.Value.TokenLifetimeMinutes, 1, 60);
        var enrollmentToken = GenerateEnrollmentToken();
        var expectedCapabilities = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure);
        var enrollment = new RunnerEnrollment
        {
            Id = Guid.NewGuid(),
            RunnerHostId = runnerHostId,
            RequestedByUserId = requestedByUserId,
            ExpectedPurpose = RunnerPurposes.Infrastructure,
            ExpectedCapabilitiesJson = RunnerCapabilities.Serialize(expectedCapabilities, RunnerPurposes.Infrastructure),
            EnrollmentTokenHash = HashToken(enrollmentToken),
            InstallationId = $"ri_{Guid.NewGuid():N}",
            Status = RunnerEnrollmentStatuses.Pending,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(lifetimeMinutes)
        };

        db.RunnerEnrollments.Add(enrollment);
        await db.SaveChangesAsync(cancellationToken);

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.RunnerEnrollment,
            EntityId = enrollment.Id,
            EventType = AuditEventTypes.RunnerEnrollmentCreated,
            Message = $"Infrastructure Runner enrollment created for host '{host.Name}'.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = requestedByUserId,
            Metadata = new
            {
                enrollment.RunnerHostId,
                enrollment.ExpiresAtUtc,
                enrollment.ExpectedPurpose,
                ExpectedCapabilities = expectedCapabilities
            }
        }, cancellationToken);

        return new RunnerEnrollmentCreatedResponse
        {
            Id = enrollment.Id,
            RunnerHostId = enrollment.RunnerHostId,
            EnrollmentToken = enrollmentToken,
            ExpiresAtUtc = enrollment.ExpiresAtUtc,
            ExpectedPurpose = enrollment.ExpectedPurpose,
            ExpectedCapabilities = expectedCapabilities,
            DockerImage = hostedRunnerOptions.Value.DockerImage
        };
    }

    public async Task<RunnerEnrollmentExchangeResponse?> ExchangeAsync(
        RunnerEnrollmentExchangeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EnrollmentToken))
        {
            return null;
        }

        if (db.Database.IsRelational())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var response = await ExchangeCoreAsync(request, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }

        return await ExchangeCoreAsync(request, cancellationToken);
    }

    public async Task<HostedRunnerEnrollmentMaterial> CreateHostedRunnerAsync(
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
            var lifetimeMinutes = Math.Clamp(options.Value.TokenLifetimeMinutes, 1, 60);
            var expiresAtUtc = now.AddMinutes(lifetimeMinutes);
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
            if (priorVersionIds.Length > 0)
            {
                var priorVersions = await db.SecretVersions.Where(x => priorVersionIds.Contains(x.Id)).ToListAsync(cancellationToken);
                var priorGrants = await db.ExecutionSecretGrants.Where(x => priorVersionIds.Contains(x.SecretVersionId)).ToListAsync(cancellationToken);
                foreach (var priorVersion in priorVersions)
                    priorVersion.RevokedAtUtc ??= now;
                foreach (var grant in priorGrants)
                    grant.RevokedAtUtc ??= now;
            }

            if (hostedRunner.ApiKeyId.HasValue)
            {
                var oldKey = await db.ApiKeys.FirstOrDefaultAsync(x => x.Id == hostedRunner.ApiKeyId.Value, cancellationToken);
                if (oldKey != null)
                    oldKey.IsActive = false;
                hostedRunner.ApiKeyId = null;
            }

            var token = GenerateEnrollmentToken();
            var enrollment = new RunnerEnrollment
            {
                Id = Guid.NewGuid(),
                RunnerHostId = runnerHostId,
                HostedRunnerId = hostedRunner.Id,
                RequestedByUserId = hostedRunner.UserId,
                ExpectedPurpose = RunnerPurposes.Managed,
                ExpectedCapabilitiesJson = RunnerCapabilities.Serialize(
                    RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed),
                    RunnerPurposes.Managed),
                EnrollmentTokenHash = HashToken(token),
                InstallationId = $"hr_{hostedRunner.Id:N}",
                Status = RunnerEnrollmentStatuses.Pending,
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAtUtc
            };
            db.RunnerEnrollments.Add(enrollment);

            var enrollmentVersion = await secretStore.CreateVersionAsync(
                new SecretOwner(SecretOwnerTypes.HostedRunner, hostedRunner.Id),
                "hosted-runner-enrollment",
                token,
                expiresAtUtc,
                cancellationToken);
            enrollment.SecretVersionId = enrollmentVersion.SecretVersionId;
            var grantId = await secretGrantService.CreateGrantAsync(
                enrollmentVersion.SecretVersionId,
                new SecretGrantScope(
                    operationId,
                    InfrastructureAppTrust.PlatformAppId,
                    actionKey,
                    infrastructureRunnerId,
                    runnerHostId,
                    SecretOwnerTypes.HostedRunner,
                    hostedRunner.Id),
                "AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN",
                expiresAtUtc,
                cancellationToken,
                SecretDeliveryPolicies.SingleDelivery);
            await db.SaveChangesAsync(cancellationToken);

            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Created,
                EntityType = AuditEntityTypes.RunnerEnrollment,
                EntityId = enrollment.Id,
                EventType = AuditEventTypes.RunnerEnrollmentCreated,
                Message = $"One-time enrollment created for hosted Runner '{hostedRunner.Name}'.",
                Outcome = AuditOutcomes.Success,
                ActorUserId = hostedRunner.UserId,
                TargetUserId = hostedRunner.UserId,
                Metadata = new { hostedRunner.Id, OperationId = operationId, GrantId = grantId, expiresAtUtc }
            }, cancellationToken);

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
            return new(enrollment.Id, grantId, expiresAtUtc);
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    public async Task<bool> RevokeAsync(
        Guid runnerHostId,
        Guid enrollmentId,
        Guid requestedByUserId,
        CancellationToken cancellationToken)
    {
        var enrollment = await db.RunnerEnrollments
            .FirstOrDefaultAsync(
                x => x.Id == enrollmentId && x.RunnerHostId == runnerHostId,
                cancellationToken);

        if (enrollment == null)
        {
            return false;
        }

        if (enrollment.Status == RunnerEnrollmentStatuses.Pending)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Revoked;
            enrollment.FailureReason = "Revoked by an administrator.";
            await db.SaveChangesAsync(cancellationToken);

            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Updated,
                EntityType = AuditEntityTypes.RunnerEnrollment,
                EntityId = enrollment.Id,
                EventType = AuditEventTypes.RunnerEnrollmentRevoked,
                Message = "Infrastructure Runner enrollment revoked.",
                Outcome = AuditOutcomes.Success,
                ActorUserId = requestedByUserId,
                Metadata = new { enrollment.RunnerHostId }
            }, cancellationToken);
        }

        return true;
    }

    private async Task<RunnerEnrollmentExchangeResponse?> ExchangeCoreAsync(
        RunnerEnrollmentExchangeRequest request,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(request.EnrollmentToken.Trim());
        var enrollment = await db.RunnerEnrollments
            .FirstOrDefaultAsync(x => x.EnrollmentTokenHash == tokenHash, cancellationToken);

        if (enrollment == null || enrollment.Status != RunnerEnrollmentStatuses.Pending)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (enrollment.ExpiresAtUtc <= now)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Expired;
            enrollment.FailureReason = "Enrollment token expired.";
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var host = await db.RunnerHosts
            .FirstOrDefaultAsync(x => x.Id == enrollment.RunnerHostId, cancellationToken);

        if (host == null)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Failed;
            enrollment.FailureReason = "The target Runner host no longer exists.";
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (enrollment.HostedRunnerId.HasValue)
            return await ExchangeHostedRunnerAsync(enrollment, host, request, now, cancellationToken);

        if (host.InfrastructureRunnerId.HasValue)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Failed;
            enrollment.FailureReason = "The target Runner host already has an Infrastructure Runner.";
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("The target Runner host already has an Infrastructure Runner.");
        }

        var runnerName = string.IsNullOrWhiteSpace(request.RunnerName)
            ? $"infrastructure-{host.Name}"
            : request.RunnerName.Trim();
        if (runnerName.Length > 120)
        {
            return null;
        }

        var capabilities = RunnerCapabilities.Deserialize(
            enrollment.ExpectedCapabilitiesJson,
            enrollment.ExpectedPurpose);

        var (rawApiKey, apiKey) = await apiKeyService.CreateApiKeyAsync(
            enrollment.RequestedByUserId,
            $"Infrastructure Runner - {runnerName}"[..Math.Min(100, $"Infrastructure Runner - {runnerName}".Length)],
            ApiKeyScope.Runner,
            "Issued by one-time Infrastructure Runner enrollment.",
            saveChanges: false,
            source: ApiKeySource.InfrastructureRunnerEnrollment);

        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = runnerName,
            Status = RunnerConnectivityStatuses.Unknown,
            Version = request.RunnerVersion,
            MachineName = request.MachineName,
            Purpose = enrollment.ExpectedPurpose,
            CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, enrollment.ExpectedPurpose),
            InstallationId = enrollment.InstallationId
        };

        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser
        {
            RunnerId = runner.Id,
            UserId = enrollment.RequestedByUserId
        });

        host.InfrastructureRunnerId = runner.Id;
        host.InfrastructureRunnerEnrolledAtUtc = now;
        if (host.ProvisioningStatus != RunnerHostProvisioningStatuses.Manual)
        {
            host.ProvisioningStatus = RunnerHostProvisioningStatuses.Validating;
            var operation = await db.RunnerHostProvisioningOperations
                .Where(x => x.RunnerHostId == host.Id
                    && x.Status == RunnerHostProvisioningOperationStatuses.AwaitingInfrastructureRunner)
                .OrderByDescending(x => x.RequestedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (operation != null)
            {
                operation.Status = RunnerHostProvisioningOperationStatuses.Running;
            }
        }
        host.UpdatedAt = now;
        enrollment.Status = RunnerEnrollmentStatuses.Completed;
        enrollment.UsedAtUtc = now;
        enrollment.RunnerId = runner.Id;

        await db.SaveChangesAsync(cancellationToken);

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Succeeded,
            EntityType = AuditEntityTypes.RunnerEnrollment,
            EntityId = enrollment.Id,
            EventType = AuditEventTypes.RunnerEnrollmentCompleted,
            Message = $"Infrastructure Runner '{runner.Name}' enrolled successfully.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = enrollment.RequestedByUserId,
            Metadata = new
            {
                enrollment.RunnerHostId,
                enrollment.RunnerId,
                runner.Name,
                runner.MachineName,
                runner.Version,
                runner.Purpose,
                Capabilities = capabilities,
                apiKey.KeyPrefix
            }
        }, cancellationToken);

        logger.LogInformation(
            "Infrastructure Runner enrolled for host {RunnerHostId} as runner {RunnerId}",
            enrollment.RunnerHostId,
            runner.Id);

        return new RunnerEnrollmentExchangeResponse
        {
            RunnerId = runner.Id,
            RunnerHostId = enrollment.RunnerHostId,
            RunnerName = runner.Name,
            ApiKey = rawApiKey,
            Purpose = runner.Purpose,
            Capabilities = capabilities,
            InstallationId = enrollment.InstallationId
        };
    }

    private async Task<RunnerEnrollmentExchangeResponse?> ExchangeHostedRunnerAsync(
        RunnerEnrollment enrollment,
        RunnerHost host,
        RunnerEnrollmentExchangeRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var hostedRunner = await db.HostedRunners
            .FirstOrDefaultAsync(x => x.Id == enrollment.HostedRunnerId && x.DeletedAt == null, cancellationToken);
        if (hostedRunner == null
            || hostedRunner.RunnerHostId != host.Id
            || hostedRunner.UserId != enrollment.RequestedByUserId
            || host.InfrastructureRunnerId == null
            || hostedRunner.Status is not (HostedRunnerStatuses.Provisioning or HostedRunnerStatuses.Reprovisioning))
        {
            enrollment.Status = RunnerEnrollmentStatuses.Failed;
            enrollment.FailureReason = "The hosted Runner enrollment scope is no longer valid.";
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var runnerName = string.IsNullOrWhiteSpace(request.RunnerName)
            ? hostedRunner.Name
            : request.RunnerName.Trim();
        if (runnerName.Length > 120)
            return null;
        var capabilities = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed);
        var (rawApiKey, apiKey) = await apiKeyService.CreateApiKeyAsync(
            hostedRunner.UserId,
            $"Hosted Runner - {runnerName}"[..Math.Min(100, $"Hosted Runner - {runnerName}".Length)],
            ApiKeyScope.Runner,
            "Issued after one-time hosted Runner enrollment.",
            saveChanges: false,
            source: ApiKeySource.RunnerProvisioning);
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = runnerName,
            Status = RunnerConnectivityStatuses.Unknown,
            Version = request.RunnerVersion,
            MachineName = request.MachineName,
            Purpose = RunnerPurposes.Managed,
            CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, RunnerPurposes.Managed),
            InstallationId = enrollment.InstallationId
        };
        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = hostedRunner.UserId });
        hostedRunner.RunnerId = runner.Id;
        hostedRunner.ApiKeyId = apiKey.Id;
        hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = now;
        enrollment.Status = RunnerEnrollmentStatuses.Completed;
        enrollment.UsedAtUtc = now;
        enrollment.RunnerId = runner.Id;

        if (enrollment.SecretVersionId.HasValue)
        {
            var version = await db.SecretVersions.FirstOrDefaultAsync(x => x.Id == enrollment.SecretVersionId, cancellationToken);
            if (version != null)
                version.RevokedAtUtc ??= now;
            var grants = await db.ExecutionSecretGrants
                .Where(x => x.SecretVersionId == enrollment.SecretVersionId && x.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var grant in grants)
            {
                if (grant.LeasedAtUtc.HasValue)
                    grant.ConsumedAtUtc ??= now;
                grant.RevokedAtUtc = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Succeeded,
            EntityType = AuditEntityTypes.RunnerEnrollment,
            EntityId = enrollment.Id,
            EventType = AuditEventTypes.RunnerEnrollmentCompleted,
            Message = $"Hosted Runner '{hostedRunner.Name}' exchanged its one-time enrollment credential.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = hostedRunner.UserId,
            TargetUserId = hostedRunner.UserId,
            Metadata = new { HostedRunnerId = hostedRunner.Id, RunnerId = runner.Id, RunnerHostId = host.Id, apiKey.KeyPrefix }
        }, cancellationToken);

        logger.LogInformation("Hosted Runner {HostedRunnerId} enrolled as Runner {RunnerId}", hostedRunner.Id, runner.Id);
        return new RunnerEnrollmentExchangeResponse
        {
            RunnerId = runner.Id,
            RunnerHostId = host.Id,
            RunnerName = runner.Name,
            ApiKey = rawApiKey,
            Purpose = runner.Purpose,
            Capabilities = capabilities,
            InstallationId = enrollment.InstallationId
        };
    }

    private static string GenerateEnrollmentToken()
        => $"re_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}"
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
