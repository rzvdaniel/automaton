using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers.Admin;

/// <summary>
/// Admin endpoint for app AI activity statistics.
/// Aggregates AI chat session metrics across ALL users for every app.
/// </summary>
[ApiController]
[Route("api/admin/app-activity")]
[Authorize(Policy = "Administrators")]
public class AdminAppActivityController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminAppActivityController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Returns all apps with aggregated AI chat stats, sorted by total tokens descending.
    /// Includes apps with zero activity so admins see the full picture.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<AppActivityDto>>> GetAppActivity()
    {
        // Load all apps
        var apps = await _dbContext.WebApps
            .Select(a => new { a.Id, a.Name })
            .ToListAsync();

        // Load all WebApp sessions aggregated per app (across all users)
        var sessionStats = await _dbContext.AiChatSessions
            .Where(s => s.ResourceType == ChatResourceType.WebApp)
            .GroupBy(s => s.ResourceId)
            .Select(g => new
            {
                AppId = g.Key,
                TotalPromptsSubmitted = g.Sum(s => s.TotalPromptsSubmitted),
                TotalApiCalls = g.Sum(s => s.TotalApiCalls),
                TotalTokens = g.Sum(s => s.TotalTokens),
                InputTokens = g.Sum(s => s.TotalInputTokens),
                OutputTokens = g.Sum(s => s.TotalOutputTokens),
                SessionCount = g.Count(),
                LastActivityAt = g.Max(s => (DateTime?)s.UpdatedAt),
            })
            .ToListAsync();

        var statsByAppId = sessionStats.ToDictionary(s => s.AppId);

        var result = apps
            .Select(app =>
            {
                statsByAppId.TryGetValue(app.Id, out var stats);
                return new AppActivityDto
                {
                    AppId = app.Id,
                    AppName = app.Name,
                    TotalPromptsSubmitted = stats?.TotalPromptsSubmitted ?? 0,
                    TotalApiCalls = stats?.TotalApiCalls ?? 0,
                    TotalTokens = stats?.TotalTokens ?? 0,
                    InputTokens = stats?.InputTokens ?? 0,
                    OutputTokens = stats?.OutputTokens ?? 0,
                    SessionCount = stats?.SessionCount ?? 0,
                    LastActivityAt = stats?.LastActivityAt,
                };
            })
            .OrderByDescending(a => a.TotalTokens)
            .ToList();

        return Ok(result);
    }
}
