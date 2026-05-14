using System.Text.Json;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers.Admin;

[ApiController]
[Route("api/admin/marketplace-apps")]
[Authorize(Policy = "Administrators")]
public class AdminMarketplaceAppsController : ControllerBase
{
    private const string MarketplaceCategoriesSettingKey = "Marketplace.AppCategories";
    private readonly ApplicationDbContext _dbContext;

    public AdminMarketplaceAppsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminMarketplaceAppDto>>> GetPublishedApps()
    {
        var apps = await _dbContext.WebApps
            .Where(a => a.IsPublished)
            .OrderByDescending(a => a.ViewCount)
            .ThenByDescending(a => a.IsFeatured)
            .ThenByDescending(a => a.PublishedAt)
            .Select(a => new AdminMarketplaceAppDto
            {
                Id = a.Id,
                Name = a.Name,
                Summary = a.Summary,
                PublicUrl = a.PublicUrl,
                ThumbnailUrl = a.ThumbnailUrl,
                ViewCount = a.ViewCount,
                IsFeatured = a.IsFeatured,
                Category = a.Category,
                Tags = a.Tags
            })
            .ToListAsync();

        return Ok(apps);
    }

    [HttpPost("{appId:guid}/promote")]
    public async Task<IActionResult> PromoteApp(Guid appId, [FromBody] UpdateMarketplaceAppRequest request)
    {
        var app = await _dbContext.WebApps.FirstOrDefaultAsync(x => x.Id == appId && x.IsPublished);
        if (app == null) return NotFound();

        app.IsFeatured = request.IsFeatured;
        app.Category = request.Category?.Trim();
        app.Tags = request.Tags?.Trim();
        app.Updated = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<string>>> GetCategories()
    {
        var stored = await _dbContext.Settings.FirstOrDefaultAsync(s => s.Key == MarketplaceCategoriesSettingKey);
        if (stored == null || string.IsNullOrWhiteSpace(stored.Value)) return Ok(new List<string>());
        return Ok(JsonSerializer.Deserialize<List<string>>(stored.Value) ?? new List<string>());
    }

    [HttpPut("categories")]
    public async Task<IActionResult> SaveCategories([FromBody] List<string> categories)
    {
        var cleaned = categories.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var setting = await _dbContext.Settings.FirstOrDefaultAsync(s => s.Key == MarketplaceCategoriesSettingKey);
        var json = JsonSerializer.Serialize(cleaned);
        if (setting == null)
        {
            _dbContext.Settings.Add(new Setting { Key = MarketplaceCategoriesSettingKey, Value = json, UpdatedAt = DateTime.UtcNow, Description = "Categories for public automaton.monster marketplace" });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
        return NoContent();
    }
}

public class AdminMarketplaceAppDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? PublicUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int ViewCount { get; set; }
    public bool IsFeatured { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
}

public class UpdateMarketplaceAppRequest
{
    public bool IsFeatured { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
}
