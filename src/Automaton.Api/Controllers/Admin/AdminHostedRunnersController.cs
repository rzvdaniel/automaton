using Automaton.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Models;
using Automaton.Api.Entities;
using Automaton.Api.Data;
using Automaton.Api.Services;

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
    private readonly UserContextService userContextService;

    public AdminHostedRunnersController(
        ApplicationDbContext db,
        HostedRunnerProvisioningService provisioningService,
        HostedRunnerManagementService managementService,
        RunnerEnrollmentService runnerEnrollmentService,
        UserContextService userContextService)
    {
        this.db = db;
        this.provisioningService = provisioningService;
        this.managementService = managementService;
        this.runnerEnrollmentService = runnerEnrollmentService;
        this.userContextService = userContextService;
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

    [HttpPost("hosts")]
    public async Task<ActionResult<RunnerHostResponse>> CreateHost(CreateRunnerHostRequest request, CancellationToken cancellationToken)
    {
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Provider = request.Provider,
            Region = request.Region,
            PublicIp = request.PublicIp,
            IsLocal = request.IsLocal,
            Status = request.Status,
            TotalCpuCores = request.TotalCpuCores,
            TotalMemoryMb = request.TotalMemoryMb,
            TotalDiskMb = request.TotalDiskMb,
            CreatedAt = DateTime.UtcNow
        };

        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/admin/hosted-runners/hosts/{host.Id}", ToResponse(host));
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
        if (request.IsLocal.HasValue) host.IsLocal = request.IsLocal.Value;
        if (request.Status != null) host.Status = request.Status;
        if (request.TotalCpuCores.HasValue) host.TotalCpuCores = request.TotalCpuCores.Value;
        if (request.TotalMemoryMb.HasValue) host.TotalMemoryMb = request.TotalMemoryMb.Value;
        if (request.TotalDiskMb.HasValue) host.TotalDiskMb = request.TotalDiskMb.Value;
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
            .Include(x => x.SubscriptionPlan)
            .Include(x => x.RunnerHost)
            .Include(x => x.Runner)
            .Include(x => x.Deployments.OrderByDescending(d => d.StartedAt).Take(1))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(runners.Select(ToResponse));
    }

    [HttpPost("users/{userId:guid}/provision")]
    public async Task<ActionResult<HostedRunnerResponse>> ProvisionForUser(Guid userId, ProvisionHostedRunnerRequest request, CancellationToken cancellationToken)
    {
        var planId = request.PlanId;
        if (planId == null)
        {
            planId = await db.Users
                .Where(x => x.Id == userId)
                .Select(x => x.SubscriptionPlanId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (planId == null)
        {
            return BadRequest("User has no subscription plan.");
        }

        var hostedRunner = await provisioningService.EnsureHostedRunnerForUserPlanAsync(userId, planId.Value, cancellationToken);
        if (hostedRunner == null)
        {
            return BadRequest("The selected plan does not include hosted runners.");
        }

        var responseRunner = await db.HostedRunners
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.SubscriptionPlan)
            .Include(x => x.RunnerHost)
            .Include(x => x.Runner)
            .Include(x => x.Deployments.OrderByDescending(d => d.StartedAt).Take(1))
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
    public async Task<IActionResult> DeleteHostedRunner(Guid id, CancellationToken cancellationToken)
    {
        await managementService.DeleteAsAdminAsync(id, cancellationToken);
        return NoContent();
    }

    private async Task<HostedRunnerResponse> ReloadHostedRunnerResponseAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.SubscriptionPlan)
            .Include(x => x.RunnerHost)
            .Include(x => x.Runner)
            .Include(x => x.Deployments.OrderByDescending(d => d.StartedAt).Take(1))
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
            IsLocal = host.IsLocal,
            Status = host.Status,
            TotalCpuCores = host.TotalCpuCores,
            UsedCpuCores = assigned.Sum(x => x.CpuCores),
            TotalMemoryMb = host.TotalMemoryMb,
            UsedMemoryMb = assigned.Sum(x => x.MemoryMb),
            TotalDiskMb = host.TotalDiskMb,
            UsedDiskMb = assigned.Sum(x => x.DiskMb),
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
            ,InfrastructureEnrollmentExpiresAtUtc = host.RunnerEnrollments
                .Where(x => x.Status == RunnerEnrollmentStatuses.Pending)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => (DateTime?)x.ExpiresAtUtc)
                .FirstOrDefault()
        };
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
        var runnerConnectivityStatus = runner.Runner == null
            ? RunnerStatus.None.ToString()
            : RunnerPresenceEvaluator.DeriveStatus(runner.Runner.ConnectionId, runner.Runner.LastHeartbeatAtUtc, DateTime.UtcNow).ToString();

        return new HostedRunnerResponse
        {
            Id = runner.Id,
            UserId = runner.UserId,
            UserEmail = runner.User.Email ?? string.Empty,
            SubscriptionPlanId = runner.SubscriptionPlanId,
            SubscriptionPlanName = runner.SubscriptionPlan.Name,
            RunnerHostId = runner.RunnerHostId,
            RunnerHostName = runner.RunnerHost?.Name,
            RunnerId = runner.RunnerId,
            RunnerName = runner.Runner?.Name,
            ApiKeyId = runner.ApiKeyId,
            Name = runner.Name,
            Status = runner.Status,
            CpuCores = runner.CpuCores,
            MemoryMb = runner.MemoryMb,
            DiskMb = runner.DiskMb,
            MaxApps = runner.MaxApps,
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

public record RunnerHostResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string? Region { get; init; }
    public string? PublicIp { get; init; }
    public bool IsLocal { get; init; }
    public string Status { get; init; } = RunnerHostStatuses.Active;
    public decimal TotalCpuCores { get; init; }
    public decimal UsedCpuCores { get; init; }
    public int TotalMemoryMb { get; init; }
    public int UsedMemoryMb { get; init; }
    public int TotalDiskMb { get; init; }
    public int UsedDiskMb { get; init; }
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
}

public record CreateRunnerHostRequest
{
    public string Name { get; init; } = string.Empty;
    public string Provider { get; init; } = RunnerHostProviders.Hetzner;
    public string? Region { get; init; }
    public string? PublicIp { get; init; }
    public bool IsLocal { get; init; }
    public string Status { get; init; } = RunnerHostStatuses.Active;
    public decimal TotalCpuCores { get; init; } = 2m;
    public int TotalMemoryMb { get; init; } = 4096;
    public int TotalDiskMb { get; init; } = 40960;
}

public record UpdateRunnerHostRequest
{
    public string? Name { get; init; }
    public string? Provider { get; init; }
    public string? Region { get; init; }
    public string? PublicIp { get; init; }
    public bool? IsLocal { get; init; }
    public string? Status { get; init; }
    public decimal? TotalCpuCores { get; init; }
    public int? TotalMemoryMb { get; init; }
    public int? TotalDiskMb { get; init; }
}

public record HostedRunnerResponse
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string UserEmail { get; init; } = string.Empty;
    public Guid SubscriptionPlanId { get; init; }
    public string SubscriptionPlanName { get; init; } = string.Empty;
    public Guid? RunnerHostId { get; init; }
    public string? RunnerHostName { get; init; }
    public Guid? RunnerId { get; init; }
    public string? RunnerName { get; init; }
    public Guid? ApiKeyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal CpuCores { get; init; }
    public int MemoryMb { get; init; }
    public int DiskMb { get; init; }
    public int MaxApps { get; init; }
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
    public Guid? PlanId { get; init; }
}
