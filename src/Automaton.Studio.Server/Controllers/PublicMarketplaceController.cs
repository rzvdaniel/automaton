using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/public/marketplace")]
[AllowAnonymous]
public class PublicMarketplaceController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public PublicMarketplaceController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("apps")]
    public async Task<ActionResult<object>> GetApps(
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] int take = 20)
    {
        var currentUserId = TryGetCurrentUserId();
        var query = _dbContext.WebApps
            .AsNoTracking()
            .Where(a =>
                a.IsPublished &&
                a.MarketplaceStatus == MarketplaceAppStatuses.Approved);

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(a => a.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a =>
                a.Name.ToLower().Contains(term) ||
                (a.Summary != null && a.Summary.ToLower().Contains(term)) ||
                (a.Tags != null && a.Tags.ToLower().Contains(term)));
        }

        var apps = await query
            .OrderByDescending(a => a.IsFeatured)
            .ThenByDescending(a => a.StarCount)
            .ThenByDescending(a => a.ViewCount)
            .ThenByDescending(a => a.PublishedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.Summary,
                a.Description,
                a.ThumbnailUrl,
                a.Category,
                a.Tags,
                a.ViewCount,
                a.StarCount,
                IsStarredByCurrentUser = currentUserId.HasValue &&
                    _dbContext.WebAppStars.Any(s => s.WebAppId == a.Id && s.UserId == currentUserId.Value),
                a.PublicUrl
            })
            .ToListAsync();

        return Ok(apps);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<string>>> GetCategories()
    {
        var categories = await _dbContext.MarketplaceCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync();

        return Ok(categories);
    }

    private Guid? TryGetCurrentUserId()
    {
        var userIdString = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdString, out var userId) ? userId : null;
    }
}
