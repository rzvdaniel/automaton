using Automaton.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Models;
using Automaton.Api.Entities;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Constants;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using System.Text.Json;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/hosted-runners")]
[Authorize(Policy = "Administrators")]
public class AdminHostedRunnersController : ControllerBase
{
    private readonly ApplicationDbContext db;
    private readonly HostedRunnerProvisioningService provisioningService;
    private readonly HostedRunnerManagementService managementService;
    private readonly RunnerEnrollmentService runnerEnrollmentService;
    private readonly ApiKeyService apiKeyService;
    private readonly UserContextService userContextService;
    private readonly InfrastructureRunnerHostApprovalService hostApprovalService;
    private readonly IAuditEventService auditEventService;
    private readonly IEventPublisher eventPublisher;
    private readonly ITransactionalEventExecutor transactionalEventExecutor;

    public AdminHostedRunnersController(
        ApplicationDbContext db,
        HostedRunnerProvisioningService provisioningService,
        HostedRunnerManagementService managementService,
        RunnerEnrollmentService runnerEnrollmentService,
        ApiKeyService apiKeyService,
        UserContextService userContextService,
        InfrastructureRunnerHostApprovalService hostApprovalService,
        IAuditEventService auditEventService,
        IEventPublisher eventPublisher,
        ITransactionalEventExecutor transactionalEventExecutor)
    {
        this.db = db;
        this.provisioningService = provisioningService;
        this.managementService = managementService;
        this.runnerEnrollmentService = runnerEnrollmentService;
        this.apiKeyService = apiKeyService;
        this.userContextService = userContextService;
        this.hostApprovalService = hostApprovalService;
        this.auditEventService = auditEventService;
        this.eventPublisher = eventPublisher;
        this.transactionalEventExecutor = transactionalEventExecutor;
    }

    [HttpPost("runners/{id:guid}/verify-storage")]
    public async Task<ActionResult> VerifyStorage(Guid id, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken);
        if (runner == null) return NotFound();
        var window = DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(5).Ticks;
        var key = $"storage-verification:{id:N}:admin:{window}";
        var existing = await db.HostedRunnerStorageVerifications
            .FirstOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (existing != null) return Accepted(new { existing.Id, existing.Status });
        var verification = new HostedRunnerStorageVerification
        {
            Id = Guid.NewGuid(), HostedRunnerId = id, IdempotencyKey = key,
            Status = StorageVerificationStatuses.Pending, Reason = "AdministratorRequested",
            RequestedAtUtc = DateTime.UtcNow
        };
        db.HostedRunnerStorageVerifications.Add(verification);
        runner.StorageVerificationRequestedAtUtc = verification.RequestedAtUtc;
        await db.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.HostedRunner,
            EntityId = runner.Id,
            EventType = AuditEventTypes.HostedRunnerStorageVerificationRequested,
            Outcome = AuditOutcomes.Success,
            Message = $"Storage verification was requested for hosted Runner '{runner.Name}'.",
            TargetUserId = runner.UserId,
            Metadata = new { VerificationId = verification.Id, verification.Reason, runner.RunnerHostId }
        }, cancellationToken);
        return Accepted(new { verification.Id, verification.Status });
    }

    [HttpPost("runners/{id:guid}/suspend")]
    public Task<ActionResult> Suspend(Guid id, CancellationToken cancellationToken) => QueueEnforcementActionAsync(id, StorageEnforcementActions.SuspendHostedRunner, cancellationToken);

    [HttpPost("runners/{id:guid}/resume")]
    public Task<ActionResult> Resume(Guid id, CancellationToken cancellationToken) => QueueEnforcementActionAsync(id, StorageEnforcementActions.ResumeHostedRunner, cancellationToken);

    [HttpPost("runners/{id:guid}/extend-grace")]
    public async Task<ActionResult> ExtendGrace(Guid id, ExtendStorageGracePeriodRequest request, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken);
        if (runner == null) return NotFound();
        if (runner.EnforcementState is HostedRunnerEnforcementStates.Suspended or HostedRunnerEnforcementStates.ResumePending or HostedRunnerEnforcementStates.SuspensionPending)
            return Conflict(new { message = "Extend grace is unavailable while a suspension or resume operation is active." });
        runner.EnforcementState = HostedRunnerEnforcementStates.GracePeriod;
        runner.GracePeriodEndsAtUtc = DateTime.UtcNow.AddHours(Math.Clamp(request.Hours, 1, 720));
        runner.LastEnforcementReason = "Grace period extended by an administrator.";
        runner.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await WriteEnforcementAuditAsync(runner, StorageTelemetryAuditEvents.EnforcementGraceExtended, "Grace period extended by an administrator.", cancellationToken);
        return Ok(new { runner.Id, runner.EnforcementState, runner.GracePeriodEndsAtUtc });
    }

    [HttpPost("runners/{id:guid}/clear-enforcement")]
    public async Task<ActionResult> ClearEnforcement(Guid id, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken);
        if (runner == null) return NotFound();
        if (runner.EnforcementState is HostedRunnerEnforcementStates.Suspended or HostedRunnerEnforcementStates.ResumePending or HostedRunnerEnforcementStates.SuspensionPending)
            return Conflict(new { message = "Resume the Runner and complete its lifecycle operation before clearing enforcement state." });
        runner.EnforcementState = HostedRunnerEnforcementStates.None;
        runner.OverLimitSinceUtc = null;
        runner.GracePeriodEndsAtUtc = null;
        runner.LastEnforcementReason = "Enforcement state cleared by an administrator.";
        runner.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await WriteEnforcementAuditAsync(runner, StorageTelemetryAuditEvents.EnforcementCleared, "Enforcement state cleared by an administrator.", cancellationToken);
        return Ok(new { runner.Id, runner.EnforcementState });
    }

    private async Task<ActionResult> QueueEnforcementActionAsync(Guid id, string actionKey, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners.Include(x => x.RunnerHost).FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken);
        if (runner == null) return NotFound();
        var action = actionKey == StorageEnforcementActions.SuspendHostedRunner ? HostedRunnerDeploymentActions.Suspend : HostedRunnerDeploymentActions.Resume;
        var existing = await db.HostedRunnerDeployments.FirstOrDefaultAsync(x => x.HostedRunnerId == id && x.Action == action &&
            (x.Status == HostedRunnerDeploymentStatuses.Pending || x.Status == HostedRunnerDeploymentStatuses.Queued || x.Status == HostedRunnerDeploymentStatuses.Dispatching || x.Status == HostedRunnerDeploymentStatuses.Executing || x.Status == HostedRunnerDeploymentStatuses.Running || x.Status == HostedRunnerDeploymentStatuses.WaitingForReconnect), cancellationToken);
        if (existing != null) return Accepted(new { existing.Id, existing.Status });
        if (runner.RunnerHostId == null) return BadRequest(new { message = "The hosted Runner has no assigned Runner Host." });
        if (action == HostedRunnerDeploymentActions.Resume && runner.EnforcementState != HostedRunnerEnforcementStates.Suspended)
            return Conflict(new { message = "Resume is only available for a suspended Runner." });
        if (action == HostedRunnerDeploymentActions.Suspend && (runner.EnforcementState == HostedRunnerEnforcementStates.Suspended || runner.EnforcementState == HostedRunnerEnforcementStates.ResumePending || runner.EnforcementState == HostedRunnerEnforcementStates.SuspensionPending))
            return Conflict(new { message = "The Runner is already suspended or has a suspension operation in progress." });
        var operation = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = id, Action = action, Status = HostedRunnerDeploymentStatuses.Pending, StartedAt = DateTime.UtcNow, Logs = "Administrator requested storage enforcement action." };
        var message = new InfrastructureActionExecutionRequestedEvent
        {
            OperationId = operation.Id, HostedRunnerId = id, UserId = runner.UserId, RunnerHostId = runner.RunnerHostId.Value,
            ActionKey = actionKey,
            InputJson = JsonSerializer.Serialize(new { operationId = operation.Id, hostedRunnerId = id, expectedContainerName = runner.ContainerName }),
            CorrelationId = $"admin-storage-enforcement:{operation.Id:N}"
        };
        await transactionalEventExecutor.ExecuteAsync(async token =>
        {
            db.HostedRunnerDeployments.Add(operation);
            runner.EnforcementState = action == HostedRunnerDeploymentActions.Suspend
                ? HostedRunnerEnforcementStates.SuspensionPending
                : HostedRunnerEnforcementStates.ResumePending;
            runner.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
            await eventPublisher.PublishAsync(message, token);
        }, cancellationToken);
        await WriteEnforcementAuditAsync(runner,
            actionKey == StorageEnforcementActions.SuspendHostedRunner ? StorageTelemetryAuditEvents.EnforcementSuspensionRequested : StorageTelemetryAuditEvents.EnforcementResumeRequested,
            $"Administrator requested '{actionKey}'.", cancellationToken);
        return Accepted(new { operation.Id, operation.Status });
    }

    private Task WriteEnforcementAuditAsync(HostedRunner runner, string eventType, string message, CancellationToken cancellationToken) =>
        auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Changed, EntityType = AuditEntityTypes.HostedRunner, EntityId = runner.Id,
            EventType = eventType, Outcome = AuditOutcomes.Success, Message = message,
            TargetUserId = runner.UserId, Metadata = new { runner.EnforcementState, runner.StorageState }
        }, cancellationToken);

    [HttpPost("infrastructure-runner/enrollment-keys")]
    public async Task<ActionResult<InfrastructureRunnerEnrollmentKeyResponse>> CreateInfrastructureRunnerEnrollmentKey(
        CancellationToken cancellationToken)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        var (rawKey, entity) = await apiKeyService.CreateApiKeyAsync(
            userContextService.GetUserId(),
            "Infrastructure Runner enrollment",
            ApiKeyScope.InfrastructureRunnerEnroll,
            "Single-use credential for Infrastructure Runner host self-registration.",
            expiresAt,
            saveChanges: true,
            maximumUses: 1,
            source: ApiKeySource.InfrastructureRunnerEnrollment);

        return Ok(new InfrastructureRunnerEnrollmentKeyResponse
        {
            Id = entity.Id,
            ApiKey = rawKey,
            KeyPrefix = entity.KeyPrefix,
            ExpiresAtUtc = expiresAt,
            CreatedAtUtc = entity.CreatedAt,
            Status = "Pending",
            IsActive = entity.IsActive,
            MaximumUses = entity.MaximumUses ?? 1
        });
    }

    [HttpGet("infrastructure-runner/enrollment-keys")]
    public async Task<ActionResult<IEnumerable<InfrastructureRunnerEnrollmentKeyResponse>>> GetInfrastructureRunnerEnrollmentKeys(
        CancellationToken cancellationToken)
    {
        var keys = await apiKeyService.GetInfrastructureRunnerEnrollmentKeysAsync(cancellationToken);
        return Ok(keys.Select(key => new InfrastructureRunnerEnrollmentKeyResponse
        {
            Id = key.Id,
            KeyPrefix = key.KeyPrefix,
            CreatedAtUtc = key.CreatedAt,
            ExpiresAtUtc = key.ExpiresAt ?? key.CreatedAt,
            UsedAtUtc = key.UsedAt,
            UseCount = key.UseCount,
            MaximumUses = key.MaximumUses ?? 1,
            IsActive = key.IsActive,
            Status = GetEnrollmentKeyStatus(key)
        }));
    }

    [HttpDelete("infrastructure-runner/enrollment-keys/{id:guid}")]
    public async Task<IActionResult> RevokeInfrastructureRunnerEnrollmentKey(Guid id, CancellationToken cancellationToken)
    {
        var revoked = await apiKeyService.RevokeInfrastructureRunnerEnrollmentKeyAsync(
            id,
            userContextService.GetUserId(),
            cancellationToken);
        return revoked ? NoContent() : NotFound();
    }

    [HttpGet("hosts")]
    public async Task<ActionResult<IEnumerable<RunnerHostResponse>>> GetHosts(CancellationToken cancellationToken)
    {
        var hosts = await db.RunnerHosts
            .AsNoTracking()
            .Include(x => x.HostedRunners)
            .Include(x => x.InfrastructureRunner)
            .Include(x => x.RunnerEnrollments)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(hosts.Select(ToResponse));
    }

    [HttpPost("hosts/{id:guid}/infrastructure-runner/enrollments")]
    public async Task<ActionResult<RunnerEnrollmentCreatedResponse>> CreateInfrastructureRunnerEnrollment(
        Guid id,
        CancellationToken cancellationToken)
    {
        var enrollment = await runnerEnrollmentService.CreateAsync(
            id,
            userContextService.GetUserId(),
            cancellationToken);

        return Ok(enrollment);
    }

    [HttpDelete("hosts/{id:guid}/infrastructure-runner/enrollments/{enrollmentId:guid}")]
    public async Task<IActionResult> RevokeInfrastructureRunnerEnrollment(
        Guid id,
        Guid enrollmentId,
        CancellationToken cancellationToken)
    {
        var revoked = await runnerEnrollmentService.RevokeAsync(
            id,
            enrollmentId,
            userContextService.GetUserId(),
            cancellationToken);

        return revoked ? NoContent() : NotFound();
    }

    [HttpPost("hosts/{id:guid}/approve")]
    public async Task<ActionResult<RunnerHostResponse>> ApproveHost(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var host = await hostApprovalService.ApproveAsync(id, userContextService.GetUserId(), cancellationToken);
            return Ok(ToResponse(host));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("hosts/{id:guid}/disable-scheduling")]
    public async Task<ActionResult<RunnerHostResponse>> DisableHostScheduling(Guid id, CancellationToken cancellationToken)
    {
        var host = await hostApprovalService.DisableSchedulingAsync(id, userContextService.GetUserId(), cancellationToken);
        return host == null ? NotFound() : Ok(ToResponse(host));
    }

    [HttpPost("hosts/{id:guid}/revoke-infrastructure-runner")]
    public async Task<ActionResult<RunnerHostResponse>> RevokeInfrastructureRunner(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var host = await hostApprovalService.RevokeAsync(id, userContextService.GetUserId(), cancellationToken);
            return host == null ? NotFound() : Ok(ToResponse(host));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("hosts/{id:guid}")]
    public async Task<ActionResult<RunnerHostResponse>> UpdateHost(Guid id, UpdateRunnerHostRequest request, CancellationToken cancellationToken)
    {
        var host = await db.RunnerHosts
            .Include(x => x.HostedRunners)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (host == null)
        {
            return NotFound();
        }

        if (request.Name != null) host.Name = request.Name;
        if (request.Provider != null) host.Provider = request.Provider;
        if (request.Region != null) host.Region = request.Region == string.Empty ? null : request.Region;
        if (request.PublicIp != null) host.PublicIp = request.PublicIp == string.Empty ? null : request.PublicIp;
        if (request.PrivateIp != null) host.PrivateIp = request.PrivateIp == string.Empty ? null : request.PrivateIp;
        if (request.ProvisioningTransport != null) host.ProvisioningTransport = request.ProvisioningTransport;
        if (request.RuntimeConnectivityMode != null) host.RuntimeConnectivityMode = request.RuntimeConnectivityMode;
        if (request.HostedRunnerSchedulingEnabled.HasValue)
        {
            if (request.HostedRunnerSchedulingEnabled.Value
                && host.RuntimeConnectivityMode == RunnerHostRuntimeConnectivityModes.PublicHostPort
                && !RunnerHostRuntimeAddressValidator.TryValidatePublicIpv4(host.PublicIp, out var addressError))
            {
                return Conflict(new { message = addressError });
            }
            if (request.HostedRunnerSchedulingEnabled.Value
                && (host.ProvisioningStatus != RunnerHostProvisioningStatuses.Ready
                    || !host.ValidatedAtUtc.HasValue
                    || !host.ApprovedAtUtc.HasValue
                    || host.Status != RunnerHostStatuses.Active
                    || host.ProvisioningTransport != RunnerHostProvisioningTransports.InfrastructureRunner
                    || !host.InfrastructureRunnerId.HasValue))
            {
                return Conflict(new { message = "A host must be Ready, validated, approved, active, and bound to an Infrastructure Runner before scheduling can be enabled." });
            }
            host.HostedRunnerSchedulingEnabled = request.HostedRunnerSchedulingEnabled.Value;
        }
        if (request.Status != null) host.Status = request.Status;
        if (request.TotalCpuCores.HasValue) host.TotalCpuCores = request.TotalCpuCores.Value;
        if (request.ReservedCpuCores.HasValue) host.ReservedCpuCores = request.ReservedCpuCores.Value;
        if (request.TotalMemoryMb.HasValue) host.TotalMemoryMb = request.TotalMemoryMb.Value;
        if (request.ReservedMemoryMb.HasValue) host.ReservedMemoryMb = request.ReservedMemoryMb.Value;
        if (request.TotalDiskMb.HasValue) host.TotalDiskMb = request.TotalDiskMb.Value;
        if (request.ReservedDiskMb.HasValue) host.ReservedDiskMb = request.ReservedDiskMb.Value;
        host.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(host));
    }

    [HttpGet("runners")]
    public async Task<ActionResult<IEnumerable<HostedRunnerResponse>>> GetHostedRunners(CancellationToken cancellationToken)
    {
        var runners = await db.HostedRunners
            .AsNoTracking()
            .Where(x => x.DeletedAt == null)
            .Include(x => x.User)
            .Include(x => x.RunnerHost)
            .Include(x => x.Runner)
            .Include(x => x.Deployments.OrderByDescending(d => d.StartedAt).Take(1))
            .Include(x => x.StorageVerifications.OrderByDescending(v => v.RequestedAtUtc).Take(1))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(runners.Select(ToResponse));
    }

    [HttpPost("users/{userId:guid}/provision")]
    public async Task<ActionResult<HostedRunnerResponse>> ProvisionForUser(Guid userId, ProvisionHostedRunnerRequest request, CancellationToken cancellationToken)
    {
        HostedRunner? hostedRunner;
        try
        {
            hostedRunner = await provisioningService.EnsureHostedRunnerAsync(userId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        if (hostedRunner == null) return BadRequest(new { message = "Hosted runner provisioning could not be prepared." });

        var responseRunner = await db.HostedRunners
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.RunnerHost)
            .Include(x => x.Runner)
            .Include(x => x.Deployments.OrderByDescending(d => d.StartedAt).Take(1))
            .Include(x => x.StorageVerifications.OrderByDescending(v => v.RequestedAtUtc).Take(1))
            .Where(x => x.Id == hostedRunner.Id)
            .FirstAsync(cancellationToken);

        return Ok(ToResponse(responseRunner));
    }

    [HttpPost("runners/{id:guid}/restart")]
    public async Task<ActionResult<HostedRunnerResponse>> RestartHostedRunner(Guid id, CancellationToken cancellationToken)
    {
        await managementService.RestartAsAdminAsync(id, cancellationToken);
        return Ok(await ReloadHostedRunnerResponseAsync(id, cancellationToken));
    }

    [HttpPost("runners/{id:guid}/reprovision")]
    public async Task<ActionResult<HostedRunnerResponse>> ReprovisionHostedRunner(Guid id, CancellationToken cancellationToken)
    {
        await managementService.ReprovisionAsAdminAsync(id, cancellationToken);
        return Ok(await ReloadHostedRunnerResponseAsync(id, cancellationToken));
    }

    [HttpDelete("runners/{id:guid}")]
    public async Task<IActionResult> DeleteHostedRunner(
        Guid id,
        DeleteHostedRunnerRequest? request,
        CancellationToken cancellationToken)
    {
        if (!HostedRunnerDataDispositions.IsValid(request?.DataDisposition))
        {
            return BadRequest(new { message = "Data disposition must be 'preserve' or 'delete'." });
        }

        await managementService.DeleteAsAdminAsync(id, request!.DataDisposition, cancellationToken);
        return NoContent();
    }

    private async Task<HostedRunnerResponse> ReloadHostedRunnerResponseAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.RunnerHost)
            .Include(x => x.Runner)
            .Include(x => x.Deployments.OrderByDescending(d => d.StartedAt).Take(1))
            .Include(x => x.StorageVerifications.OrderByDescending(v => v.RequestedAtUtc).Take(1))
            .FirstAsync(x => x.Id == hostedRunnerId, cancellationToken);

        return ToResponse(runner);
    }

    private static RunnerHostResponse ToResponse(RunnerHost host)
    {
        var assigned = host.HostedRunners
            .Where(x => x.DeletedAt == null && x.Status is HostedRunnerStatuses.Pending or HostedRunnerStatuses.Provisioning or HostedRunnerStatuses.Running)
            .ToList();

        return new RunnerHostResponse
        {
            Id = host.Id,
            Name = host.Name,
            Provider = host.Provider,
            Region = host.Region,
            PublicIp = host.PublicIp,
            PrivateIp = host.PrivateIp,
            ProviderResourceId = host.ProviderResourceId,
            ProviderServerType = host.ProviderServerType,
            ProviderImageId = host.ProviderImageId,
            ProvisioningTransport = host.ProvisioningTransport,
            RuntimeConnectivityMode = host.RuntimeConnectivityMode,
            HostedRunnerSchedulingEnabled = host.HostedRunnerSchedulingEnabled,
            Status = host.Status,
            ProvisioningStatus = host.ProvisioningStatus,
            ProvisioningStartedAtUtc = host.ProvisioningStartedAtUtc,
            InfrastructureRunnerEnrolledAtUtc = host.InfrastructureRunnerEnrolledAtUtc,
            ValidatedAtUtc = host.ValidatedAtUtc,
            ApprovedAtUtc = host.ApprovedAtUtc,
            ApprovedByUserId = host.ApprovedByUserId,
            ProvisioningFailureCode = host.ProvisioningFailureCode,
            ProvisioningFailureMessage = host.ProvisioningFailureMessage,
            IsSchedulingEligible = host.IsSchedulingEligible,
            TotalCpuCores = host.TotalCpuCores,
            ReservedCpuCores = host.ReservedCpuCores,
            UsedCpuCores = assigned.Sum(x => x.SpecificationSnapshot.Resources.CpuLimit),
            TotalMemoryMb = host.TotalMemoryMb,
            ReservedMemoryMb = host.ReservedMemoryMb,
            UsedMemoryMb = assigned.Sum(x => x.SpecificationSnapshot.Resources.MemoryLimitMb),
            TotalDiskMb = host.TotalDiskMb,
            ReservedDiskMb = host.ReservedDiskMb,
            UsedDiskMb = assigned.Sum(x => x.SpecificationSnapshot.Resources.DiskLimitMb),
            StorageMeasuredAtUtc = host.StorageMeasuredAtUtc,
            StorageAvailableBytes = host.StorageAvailableBytes,
            StorageReserveRequiredBytes = host.StorageReserveRequiredBytes,
            StorageState = host.StorageState,
            StorageMeasurementStatus = host.StorageMeasurementStatus,
            StorageMeasurementError = host.StorageMeasurementError,
            HostedRunnerCount = assigned.Count,
            CreatedAt = host.CreatedAt,
            UpdatedAt = host.UpdatedAt,
            InfrastructureRunnerId = host.InfrastructureRunnerId,
            InfrastructureRunnerName = host.InfrastructureRunner?.Name,
            InfrastructureRunnerPurpose = host.InfrastructureRunner?.Purpose,
            InfrastructureRunnerStatus = host.InfrastructureRunner == null
                ? RunnerStatus.None.ToString()
                : RunnerPresenceEvaluator.DeriveStatus(
                    host.InfrastructureRunner.ConnectionId,
                    host.InfrastructureRunner.LastHeartbeatAtUtc,
                    DateTime.UtcNow).ToString(),
            InfrastructureRunnerLastHeartbeatAtUtc = host.InfrastructureRunner?.LastHeartbeatAtUtc
            ,InfrastructureRunnerOnboardingStatus = GetInfrastructureRunnerOnboardingStatus(host)
            ,InfrastructureRunnerVersion = host.InfrastructureRunner?.Version
            ,InfrastructureRunnerInstallationId = host.InfrastructureRunner?.InstallationId
            ,InfrastructureRunnerCapabilities = host.InfrastructureRunner == null
                ? Array.Empty<string>()
                : RunnerCapabilities.Deserialize(host.InfrastructureRunner.CapabilitiesJson, host.InfrastructureRunner.Purpose)
            ,LastHeartbeatAtUtc = host.LastHeartbeatAtUtc
            ,MetadataJson = host.MetadataJson
            ,HealthJson = host.HealthJson
            ,InfrastructureEnrollmentExpiresAtUtc = host.RunnerEnrollments
                .Where(x => x.Status == RunnerEnrollmentStatuses.Pending)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => (DateTime?)x.ExpiresAtUtc)
                .FirstOrDefault()
        };
    }

    private static string GetEnrollmentKeyStatus(ApiKey key)
    {
        if (!key.IsActive && key.UsedAt.HasValue)
            return "Consumed";
        if (!key.IsActive)
            return "Revoked";
        if (key.ExpiresAt.HasValue && key.ExpiresAt.Value <= DateTime.UtcNow)
            return "Expired";
        return "Pending";
    }

    private static string GetInfrastructureRunnerOnboardingStatus(RunnerHost host)
    {
        if (host.InfrastructureRunner != null)
        {
            return RunnerPresenceEvaluator.IsOnline(
                host.InfrastructureRunner.ConnectionId,
                host.InfrastructureRunner.LastHeartbeatAtUtc,
                DateTime.UtcNow)
                ? "Online"
                : "Offline";
        }

        var latestEnrollment = host.RunnerEnrollments
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        return latestEnrollment?.Status switch
        {
            RunnerEnrollmentStatuses.Pending => "EnrollmentPending",
            RunnerEnrollmentStatuses.Revoked => "Revoked",
            _ => "NotInstalled"
        };
    }

    private static HostedRunnerResponse ToResponse(HostedRunner runner)
    {
        var deployment = runner.Deployments.OrderByDescending(x => x.StartedAt).FirstOrDefault();
        var verification = runner.StorageVerifications.OrderByDescending(x => x.RequestedAtUtc).FirstOrDefault();
        var runnerConnectivityStatus = runner.Runner == null
            ? RunnerStatus.None.ToString()
            : RunnerPresenceEvaluator.DeriveStatus(runner.Runner.ConnectionId, runner.Runner.LastHeartbeatAtUtc, DateTime.UtcNow).ToString();

        return new HostedRunnerResponse
        {
            Id = runner.Id,
            UserId = runner.UserId,
            UserEmail = runner.User.Email ?? string.Empty,
            HostedRunnerSpecificationId = runner.HostedRunnerSpecificationId,
            SpecificationRevision = runner.SpecificationRevision,
            SpecificationSnapshot = runner.SpecificationSnapshot,
            PendingSpecificationSnapshot = runner.PendingSpecificationSnapshot,
            RunnerHostId = runner.RunnerHostId,
            RunnerHostName = runner.RunnerHost?.Name,
            RunnerId = runner.RunnerId,
            RunnerName = runner.Runner?.Name,
            ApiKeyId = runner.ApiKeyId,
            Name = runner.Name,
            Status = runner.Status,
            ContainerName = runner.ContainerName,
            ContainerId = runner.ContainerId,
            RuntimePort = runner.RuntimePort,
            RunnerConnectivityStatus = runnerConnectivityStatus,
            RunnerLastHeartbeatAtUtc = runner.Runner?.LastHeartbeatAtUtc,
            RunnerLastConnectedAtUtc = runner.Runner?.LastConnectedAtUtc,
            RunnerVersion = runner.Runner?.Version,
            RunnerMachineName = runner.Runner?.MachineName,
            LastError = runner.LastError,
            CreatedAt = runner.CreatedAt,
            UpdatedAt = runner.UpdatedAt,
            ProvisionedAt = runner.ProvisionedAt,
            DeletedAt = runner.DeletedAt,
            StorageAllowanceBytes = runner.StorageAllowanceBytes,
            StorageMeasuredAtUtc = runner.StorageMeasuredAtUtc,
            StorageMeasurementSource = runner.StorageMeasurementSource,
            StorageMeasurementStatus = runner.StorageMeasurementStatus,
            StorageSystemBytes = runner.StorageSystemBytes,
            StorageUserBytes = runner.StorageUserBytes,
            StorageTotalBytes = runner.StorageTotalBytes,
            StorageUsagePercent = runner.StorageUsagePercent,
            StorageState = runner.StorageState,
            StorageMeasurementError = runner.StorageMeasurementError,
            StorageVerificationRequestedAtUtc = runner.StorageVerificationRequestedAtUtc,
            StorageVerifiedAtUtc = runner.StorageVerifiedAtUtc,
            StorageVerifiedTotalBytes = runner.StorageVerifiedTotalBytes,
            StorageSelfReportedAtUtc = runner.StorageSelfReportedAtUtc,
            StorageSelfReportedTotalBytes = runner.StorageSelfReportedTotalBytes,
            StorageSelfReportedStatus = runner.StorageSelfReportedStatus,
            StorageSelfReportedError = runner.StorageSelfReportedError,
            HostStorageAvailableBytes = runner.RunnerHost?.StorageAvailableBytes,
            HostStorageReserveRequiredBytes = runner.RunnerHost?.StorageReserveRequiredBytes,
            HostStorageState = runner.RunnerHost?.StorageState,
            StorageVerificationStatus = verification?.Status,
            StorageVerificationReason = verification?.Reason,
            StorageVerificationError = verification?.Error,
            EnforcementState = runner.EnforcementState,
            OverLimitSinceUtc = runner.OverLimitSinceUtc,
            GracePeriodEndsAtUtc = runner.GracePeriodEndsAtUtc,
            SuspendedAtUtc = runner.SuspendedAtUtc,
            RecoveredAtUtc = runner.RecoveredAtUtc,
            LastEnforcementReason = runner.LastEnforcementReason,
            LastNotificationAtUtc = runner.LastNotificationAtUtc,
            LatestDeployment = deployment == null ? null : new HostedRunnerDeploymentResponse
            {
                Id = deployment.Id,
                Action = deployment.Action,
                Status = deployment.Status,
                CommandPreview = deployment.CommandPreview,
                Logs = deployment.Logs,
                Error = deployment.Error,
                StartedAt = deployment.StartedAt,
                FinishedAt = deployment.FinishedAt
            }
        };
    }
}

public sealed record DeleteHostedRunnerRequest(string DataDisposition);
public sealed record ExtendStorageGracePeriodRequest(int Hours);

public record RunnerHostResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string? Region { get; init; }
    public string? PublicIp { get; init; }
    public string? PrivateIp { get; init; }
    public string? ProviderResourceId { get; init; }
    public string? ProviderServerType { get; init; }
    public string? ProviderImageId { get; init; }
    public string ProvisioningTransport { get; init; } = RunnerHostProvisioningTransports.InfrastructureRunner;
    public string RuntimeConnectivityMode { get; init; } = RunnerHostRuntimeConnectivityModes.PublicHostPort;
    public bool HostedRunnerSchedulingEnabled { get; init; } = true;
    public string Status { get; init; } = RunnerHostStatuses.Active;
    public string ProvisioningStatus { get; init; } = RunnerHostProvisioningStatuses.Manual;
    public DateTime? ProvisioningStartedAtUtc { get; init; }
    public DateTime? InfrastructureRunnerEnrolledAtUtc { get; init; }
    public DateTime? ValidatedAtUtc { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
    public Guid? ApprovedByUserId { get; init; }
    public string? ProvisioningFailureCode { get; init; }
    public string? ProvisioningFailureMessage { get; init; }
    public bool IsSchedulingEligible { get; init; }
    public decimal TotalCpuCores { get; init; }
    public decimal ReservedCpuCores { get; init; }
    public decimal UsedCpuCores { get; init; }
    public int TotalMemoryMb { get; init; }
    public int ReservedMemoryMb { get; init; }
    public int UsedMemoryMb { get; init; }
    public int TotalDiskMb { get; init; }
    public int ReservedDiskMb { get; init; }
    public int UsedDiskMb { get; init; }
    public DateTime? StorageMeasuredAtUtc { get; init; }
    public long? StorageAvailableBytes { get; init; }
    public long? StorageReserveRequiredBytes { get; init; }
    public string StorageState { get; init; } = RunnerHostStorageStates.Unknown;
    public string StorageMeasurementStatus { get; init; } = StorageMeasurementStatuses.NeverMeasured;
    public string? StorageMeasurementError { get; init; }
    public int HostedRunnerCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public Guid? InfrastructureRunnerId { get; init; }
    public string? InfrastructureRunnerName { get; init; }
    public string? InfrastructureRunnerPurpose { get; init; }
    public string InfrastructureRunnerStatus { get; init; } = RunnerStatus.None.ToString();
    public DateTime? InfrastructureRunnerLastHeartbeatAtUtc { get; init; }
    public string InfrastructureRunnerOnboardingStatus { get; init; } = "NotInstalled";
    public string? InfrastructureRunnerVersion { get; init; }
    public string? InfrastructureRunnerInstallationId { get; init; }
    public IReadOnlyList<string> InfrastructureRunnerCapabilities { get; init; } = Array.Empty<string>();
    public DateTime? InfrastructureEnrollmentExpiresAtUtc { get; init; }
    public DateTime? LastHeartbeatAtUtc { get; init; }
    public string? MetadataJson { get; init; }
    public string? HealthJson { get; init; }
}

public record UpdateRunnerHostRequest
{
    public string? Name { get; init; }
    public string? Provider { get; init; }
    public string? Region { get; init; }
    public string? PublicIp { get; init; }
    public string? PrivateIp { get; init; }
    public string? ProvisioningTransport { get; init; }
    public string? RuntimeConnectivityMode { get; init; }
    public bool? HostedRunnerSchedulingEnabled { get; init; }
    public string? Status { get; init; }
    public decimal? TotalCpuCores { get; init; }
    public decimal? ReservedCpuCores { get; init; }
    public int? TotalMemoryMb { get; init; }
    public int? ReservedMemoryMb { get; init; }
    public int? TotalDiskMb { get; init; }
    public int? ReservedDiskMb { get; init; }
}

public record HostedRunnerResponse
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string UserEmail { get; init; } = string.Empty;
    public Guid HostedRunnerSpecificationId { get; init; }
    public int SpecificationRevision { get; init; }
    public HostedRunnerSpecificationSnapshot SpecificationSnapshot { get; init; } = new();
    public HostedRunnerSpecificationSnapshot? PendingSpecificationSnapshot { get; init; }
    public Guid? RunnerHostId { get; init; }
    public string? RunnerHostName { get; init; }
    public Guid? RunnerId { get; init; }
    public string? RunnerName { get; init; }
    public Guid? ApiKeyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string? ContainerId { get; init; }
    public int? RuntimePort { get; init; }
    public string RunnerConnectivityStatus { get; init; } = RunnerStatus.None.ToString();
    public DateTime? RunnerLastHeartbeatAtUtc { get; init; }
    public DateTime? RunnerLastConnectedAtUtc { get; init; }
    public string? RunnerVersion { get; init; }
    public string? RunnerMachineName { get; init; }
    public string? LastError { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? ProvisionedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
    public HostedRunnerDeploymentResponse? LatestDeployment { get; init; }
    public long StorageAllowanceBytes { get; init; }
    public DateTime? StorageMeasuredAtUtc { get; init; }
    public string StorageMeasurementSource { get; init; } = StorageMeasurementSources.RunnerSelfReported;
    public string StorageMeasurementStatus { get; init; } = StorageMeasurementStatuses.NeverMeasured;
    public long? StorageSystemBytes { get; init; }
    public long? StorageUserBytes { get; init; }
    public long? StorageTotalBytes { get; init; }
    public decimal? StorageUsagePercent { get; init; }
    public string StorageState { get; init; } = HostedRunnerStorageStates.Unknown;
    public string? StorageMeasurementError { get; init; }
    public DateTime? StorageVerificationRequestedAtUtc { get; init; }
    public DateTime? StorageVerifiedAtUtc { get; init; }
    public long? StorageVerifiedTotalBytes { get; init; }
    public DateTime? StorageSelfReportedAtUtc { get; init; }
    public long? StorageSelfReportedTotalBytes { get; init; }
    public string StorageSelfReportedStatus { get; init; } = StorageMeasurementStatuses.NeverMeasured;
    public string? StorageSelfReportedError { get; init; }
    public long? HostStorageAvailableBytes { get; init; }
    public long? HostStorageReserveRequiredBytes { get; init; }
    public string? HostStorageState { get; init; }
    public string? StorageVerificationStatus { get; init; }
    public string? StorageVerificationReason { get; init; }
    public string? StorageVerificationError { get; init; }
    public string EnforcementState { get; init; } = HostedRunnerEnforcementStates.None;
    public DateTime? OverLimitSinceUtc { get; init; }
    public DateTime? GracePeriodEndsAtUtc { get; init; }
    public DateTime? SuspendedAtUtc { get; init; }
    public DateTime? RecoveredAtUtc { get; init; }
    public string? LastEnforcementReason { get; init; }
    public DateTime? LastNotificationAtUtc { get; init; }
}

public record HostedRunnerDeploymentResponse
{
    public Guid Id { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? CommandPreview { get; init; }
    public string? Logs { get; init; }
    public string? Error { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
}

public record ProvisionHostedRunnerRequest
{
}
