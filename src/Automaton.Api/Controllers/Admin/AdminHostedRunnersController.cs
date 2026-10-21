using Automaton.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Models;
using Automaton.Api.Entities;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Constants;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/hosted-runners")]
[Authorize(Policy = "Administrators")]
public class AdminHostedRunnersController : ControllerBase
{
    private readonly ApplicationDbContext db;
    private readonly HostedRunnerManagementService managementService;
    private readonly UserContextService userContextService;

    public AdminHostedRunnersController(
        ApplicationDbContext db,
        HostedRunnerManagementService managementService,
        UserContextService userContextService)
    {
        this.db = db;
        this.managementService = managementService;
        this.userContextService = userContextService;
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

        return Ok(runners.Select(HostedRunnerAdminResponseMapper.ToResponse));
    }

    [HttpPost("runners/{id:guid}/restart")]
    public async Task<ActionResult<HostedRunnerResponse>> RestartHostedRunner(Guid id, CancellationToken cancellationToken)
    {
        await managementService.RestartAsAdminAsync(id, cancellationToken);
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

        return HostedRunnerAdminResponseMapper.ToResponse(runner);
    }

}
