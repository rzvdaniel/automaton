using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers.Admin;

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

        var activeRunners = await _dbContext.HostedRunners
            .AsNoTracking()
            .CountAsync(runner => runner.DeletedAt == null && runner.Status == HostedRunnerStatuses.Running, cancellationToken);

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
