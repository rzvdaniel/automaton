using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Policy = "Administrators")]
public class AdminDashboardController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminDashboardController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<AdminDashboardSummaryResponse>> GetSummary(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var todayUtc = now.Date;
        var monthStartUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var totalUsers = await _dbContext.Users
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var newRegistrations = await _dbContext.Users
            .AsNoTracking()
            .CountAsync(user => user.RegisteredAtUtc >= monthStartUtc, cancellationToken);

        var publishedApps = await _dbContext.WebApps
            .AsNoTracking()
            .CountAsync(app => app.IsPublished, cancellationToken);

        var activeRunners = await _dbContext.Runners
            .AsNoTracking()
            .CountAsync(runner => runner.Purpose == RunnerPurposes.Managed &&
                runner.ApplicationProfile == RunnerApplicationProfiles.MachineManagement &&
                runner.ConnectionId != null && runner.LastHeartbeatAtUtc != null &&
                runner.LastHeartbeatAtUtc >= now - RunnerPresenceEvaluator.OfflineThreshold, cancellationToken);

        var executionsToday = await _dbContext.FlowExecutions
            .AsNoTracking()
            .CountAsync(execution => execution.Started >= todayUtc, cancellationToken);

        var failedExecutions = await _dbContext.FlowExecutions
            .AsNoTracking()
            .CountAsync(
                execution => execution.Started >= todayUtc && execution.Status == WorkflowStatus.Error.ToString(),
                cancellationToken);

        return Ok(new AdminDashboardSummaryResponse
        {
            TotalUsers = totalUsers,
            NewRegistrations = newRegistrations,
            PublishedApps = publishedApps,
            ActiveRunners = activeRunners,
            ExecutionsToday = executionsToday,
            FailedExecutions = failedExecutions
        });
    }
}
