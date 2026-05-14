using System.Text.Json;
using Automaton.Studio.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/public/marketplace")]
[AllowAnonymous]
public class PublicMarketplaceController : ControllerBase
{
    private const string MarketplaceCategoriesSettingKey = "Marketplace.AppCategories";
    private readonly ApplicationDbContext _dbContext;

    public PublicMarketplaceController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("apps")]
    public async Task<ActionResult<object>> GetApps([FromQuery] string? category = null, [FromQuery] string? search = null, [FromQuery] int take = 20)
    {
        var query = _dbContext.WebApps.AsNoTracking().Where(a => a.IsPublished);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(a => a.Category == category);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a => a.Name.ToLower().Contains(term) || (a.Summary != null && a.Summary.ToLower().Contains(term)) || (a.Tags != null && a.Tags.ToLower().Contains(term)));
        }

        var apps = await query
            .OrderByDescending(a => a.IsFeatured)
            .ThenByDescending(a => a.ViewCount)
            .ThenByDescending(a => a.PublishedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(a => new
            {
                a.Id, a.Name, a.Summary, a.Description, a.ThumbnailUrl, a.Category, a.Tags, a.ViewCount, a.PublicUrl
            }).ToListAsync();

        return Ok(apps);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<string>>> GetCategories()
    {
        var stored = await _dbContext.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == MarketplaceCategoriesSettingKey);
        if (stored == null || string.IsNullOrWhiteSpace(stored.Value))
        {
            var inferred = await _dbContext.WebApps.AsNoTracking().Where(a => a.IsPublished && a.Category != null).Select(a => a.Category!).Distinct().OrderBy(x => x).ToListAsync();
            return Ok(inferred);
        }

        return Ok(JsonSerializer.Deserialize<List<string>>(stored.Value) ?? new List<string>());
    }
}
