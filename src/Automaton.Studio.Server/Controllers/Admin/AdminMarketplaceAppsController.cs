using System.Text.RegularExpressions;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers.Admin;

[ApiController]
[Route("api/admin/marketplace-apps")]
[Authorize(Policy = "Administrators")]
public class AdminMarketplaceAppsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminMarketplaceAppsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminMarketplaceAppDto>>> GetPublishedApps()
    {
        var apps = await _dbContext.WebApps
            .AsNoTracking()
            .Where(a => a.IsPublished)
            .OrderByDescending(a => a.IsFeatured)
            .ThenByDescending(a => a.ViewCount)
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
                Tags = a.Tags,
                MarketplaceStatus = a.MarketplaceStatus,
                MarketplaceModerationReason = a.MarketplaceModerationReason,
                MarketplaceModeratedAt = a.MarketplaceModeratedAt,
                MarketplaceModeratedByUserId = a.MarketplaceModeratedByUserId,
                OwnerDisplayName = _dbContext.WebAppUsers
                    .Where(au => au.WebAppId == a.Id)
                    .Join(
                        _dbContext.Users,
                        au => au.UserId,
                        user => user.Id,
                        (_, user) => !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : user.Name)
                    .FirstOrDefault(),
                OwnerPublicUsername = _dbContext.WebAppUsers
                    .Where(au => au.WebAppId == a.Id)
                    .Join(
                        _dbContext.Users,
                        au => au.UserId,
                        user => user.Id,
                        (_, user) => user.PublicUsername)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(apps);
    }

    [HttpPut("{appId:guid}")]
    public async Task<IActionResult> UpdateMarketplaceApp(Guid appId, [FromBody] UpdateMarketplaceAppRequest request)
    {
        var app = await _dbContext.WebApps.FirstOrDefaultAsync(x => x.Id == appId && x.IsPublished);
        if (app == null)
        {
            return NotFound();
        }

        app.IsFeatured = request.IsFeatured;
        app.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        app.MarketplaceStatus = NormalizeStatus(request.MarketplaceStatus);
        app.MarketplaceModerationReason = string.IsNullOrWhiteSpace(request.MarketplaceModerationReason)
            ? null
            : request.MarketplaceModerationReason.Trim();
        app.MarketplaceModeratedAt = DateTime.UtcNow;
        app.MarketplaceModeratedByUserId = GetCurrentUserId();
        app.Updated = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return NoContent();
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

    [HttpPut("categories")]
    public async Task<IActionResult> SaveCategories([FromBody] List<string> categories)
    {
        var cleaned = categories
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = await _dbContext.MarketplaceCategories.ToListAsync();
        var now = DateTime.UtcNow;

        for (var i = 0; i < cleaned.Count; i++)
        {
            var name = cleaned[i];
            var match = existing.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                _dbContext.MarketplaceCategories.Add(new MarketplaceCategory
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Slug = Slugify(name),
                    SortOrder = i,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                continue;
            }

            match.Name = name;
            match.Slug = Slugify(name);
            match.SortOrder = i;
            match.IsActive = true;
            match.UpdatedAt = now;
        }

        var retained = new HashSet<string>(cleaned, StringComparer.OrdinalIgnoreCase);
        foreach (var removed in existing.Where(c => !retained.Contains(c.Name)))
        {
            removed.IsActive = false;
            removed.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync();
        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var claimValue = User.FindFirst("sub")?.Value ?? User.FindFirst("userId")?.Value;
        return Guid.TryParse(claimValue, out var userId) ? userId : null;
    }

    private static string NormalizeStatus(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            MarketplaceAppStatuses.Hidden => MarketplaceAppStatuses.Hidden,
            MarketplaceAppStatuses.Banned => MarketplaceAppStatuses.Banned,
            _ => MarketplaceAppStatuses.Approved,
        };
    }

    private static string Slugify(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? Guid.NewGuid().ToString("N") : slug;
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
    public string MarketplaceStatus { get; set; } = MarketplaceAppStatuses.Approved;
    public string? MarketplaceModerationReason { get; set; }
    public DateTime? MarketplaceModeratedAt { get; set; }
    public Guid? MarketplaceModeratedByUserId { get; set; }
    public string? OwnerDisplayName { get; set; }
    public string? OwnerPublicUsername { get; set; }
}

public class UpdateMarketplaceAppRequest
{
    public bool IsFeatured { get; set; }
    public string? Category { get; set; }
    public string? MarketplaceStatus { get; set; }
    public string? MarketplaceModerationReason { get; set; }
}
