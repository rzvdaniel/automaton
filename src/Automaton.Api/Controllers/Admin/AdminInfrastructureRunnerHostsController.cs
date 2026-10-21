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

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/hosted-runners")]
[Authorize(Policy = "Administrators")]
public sealed class AdminInfrastructureRunnerHostsController(
    ApplicationDbContext db,
    RunnerEnrollmentService runnerEnrollmentService,
    ApiKeyService apiKeyService,
    UserContextService userContextService,
    InfrastructureRunnerHostApprovalService hostApprovalService) : ControllerBase
{
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
            Status = RunnerHostAdminResponseMapper.GetEnrollmentKeyStatus(key)
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

        return Ok(hosts.Select(RunnerHostAdminResponseMapper.ToResponse));
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
            return Ok(RunnerHostAdminResponseMapper.ToResponse(host));
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

    [HttpPost("hosts/{id:guid}/revoke-infrastructure-runner")]
    public async Task<ActionResult<RunnerHostResponse>> RevokeInfrastructureRunner(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var host = await hostApprovalService.RevokeAsync(id, userContextService.GetUserId(), cancellationToken);
            return host == null ? NotFound() : Ok(RunnerHostAdminResponseMapper.ToResponse(host));
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
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (host == null)
        {
            return NotFound();
        }

        ApplyHostIdentityAndConnectivity(host, request);
        if (request.Status != null) host.Status = request.Status;
        host.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(RunnerHostAdminResponseMapper.ToResponse(host));
    }

    private static void ApplyHostIdentityAndConnectivity(RunnerHost host, UpdateRunnerHostRequest request)
    {
        if (request.Name != null) host.Name = request.Name;
        if (request.Provider != null) host.Provider = request.Provider;
        if (request.Region != null) host.Region = request.Region == string.Empty ? null : request.Region;
        if (request.PublicIp != null) host.PublicIp = request.PublicIp == string.Empty ? null : request.PublicIp;
        if (request.PrivateIp != null) host.PrivateIp = request.PrivateIp == string.Empty ? null : request.PrivateIp;
        if (request.ProvisioningTransport != null) host.ProvisioningTransport = request.ProvisioningTransport;
        if (request.RuntimeConnectivityMode != null) host.RuntimeConnectivityMode = request.RuntimeConnectivityMode;
    }

}
