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
    public async Task<ActionResult<List<MarketplaceAppCardDto>>> GetApps(
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
            .Select(a => new MarketplaceAppCardDto
            {
                Id = a.Id,
                Name = a.Name,
                CreatorPublicUsername = _dbContext.WebAppUsers
                    .Where(wu => wu.WebAppId == a.Id)
                    .Join(_dbContext.Users, wu => wu.UserId, u => u.Id, (wu, u) => u)
                    .Select(u => u.PublicUsername)
                    .FirstOrDefault(),
                CreatorDisplayName = _dbContext.WebAppUsers
                    .Where(wu => wu.WebAppId == a.Id)
                    .Join(_dbContext.Users, wu => wu.UserId, u => u.Id, (wu, u) => u)
                    .Select(u => !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : u.Name)
                    .FirstOrDefault(),
                Summary = a.Summary,
                Description = a.Description,
                ThumbnailUrl = a.ThumbnailUrl ??
                    _dbContext.WebAppScreenshots
                        .Where(s => s.WebAppId == a.Id)
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.CreatedAt)
                        .Select(s => s.ImageUrl)
                        .FirstOrDefault(),
                Category = a.Category,
                Tags = a.Tags,
                ViewCount = a.ViewCount,
                StarCount = a.StarCount,
                IsStarredByCurrentUser = currentUserId.HasValue &&
                    _dbContext.WebAppStars.Any(s => s.WebAppId == a.Id && s.UserId == currentUserId.Value),
                PublicUrl = a.PublicUrl,
                PublishedAt = a.PublishedAt,
                IsFeatured = a.IsFeatured,
                Screenshots = _dbContext.WebAppScreenshots
                    .Where(s => s.WebAppId == a.Id)
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.CreatedAt)
                    .Select(s => new MarketplaceAppScreenshotDto
                    {
                        Id = s.Id,
                        ImageUrl = s.ImageUrl,
                        Caption = s.Caption,
                        SortOrder = s.SortOrder
                    })
                    .ToList()
            })
            .ToListAsync();

        return Ok(apps);
    }

    [HttpGet("apps/{appId:guid}")]
    public async Task<ActionResult<MarketplaceAppDetailsDto>> GetAppDetails(Guid appId)
    {
        var currentUserId = TryGetCurrentUserId();

        var app = await _dbContext.WebApps
            .AsNoTracking()
            .Where(a =>
                a.Id == appId &&
                a.IsPublished &&
                a.MarketplaceStatus == MarketplaceAppStatuses.Approved)
            .Select(a => new MarketplaceAppDetailsDto
            {
                Id = a.Id,
                Name = a.Name,
                CreatorPublicUsername = _dbContext.WebAppUsers
                    .Where(wu => wu.WebAppId == a.Id)
                    .Join(_dbContext.Users, wu => wu.UserId, u => u.Id, (wu, u) => u)
                    .Select(u => u.PublicUsername)
                    .FirstOrDefault(),
                CreatorDisplayName = _dbContext.WebAppUsers
                    .Where(wu => wu.WebAppId == a.Id)
                    .Join(_dbContext.Users, wu => wu.UserId, u => u.Id, (wu, u) => u)
                    .Select(u => !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : u.Name)
                    .FirstOrDefault(),
                Summary = a.Summary,
                Description = a.Description,
                ThumbnailUrl = a.ThumbnailUrl ??
                    _dbContext.WebAppScreenshots
                        .Where(s => s.WebAppId == a.Id)
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.CreatedAt)
                        .Select(s => s.ImageUrl)
                        .FirstOrDefault(),
                Category = a.Category,
                Tags = a.Tags,
                ViewCount = a.ViewCount,
                StarCount = a.StarCount,
                IsStarredByCurrentUser = currentUserId.HasValue &&
                    _dbContext.WebAppStars.Any(s => s.WebAppId == a.Id && s.UserId == currentUserId.Value),
                PublicUrl = a.PublicUrl,
                PublishedAt = a.PublishedAt,
                IsFeatured = a.IsFeatured
            })
            .FirstOrDefaultAsync();

        if (app == null)
        {
            return NotFound(new { message = "App not found or not published" });
        }

        app.Screenshots = await _dbContext.WebAppScreenshots
            .AsNoTracking()
            .Where(s => s.WebAppId == app.Id)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAt)
            .Select(s => new MarketplaceAppScreenshotDto
            {
                Id = s.Id,
                ImageUrl = s.ImageUrl,
                Caption = s.Caption,
                SortOrder = s.SortOrder
            })
            .ToListAsync();

        app.RelatedApps = await _dbContext.WebApps
            .AsNoTracking()
            .Where(a =>
                a.Id != app.Id &&
                a.IsPublished &&
                a.MarketplaceStatus == MarketplaceAppStatuses.Approved &&
                a.Category != null &&
                a.Category == app.Category)
            .OrderByDescending(a => a.IsFeatured)
            .ThenByDescending(a => a.StarCount)
            .ThenByDescending(a => a.ViewCount)
            .ThenByDescending(a => a.PublishedAt)
            .Take(4)
            .Select(a => new MarketplaceAppCardDto
            {
                Id = a.Id,
                Name = a.Name,
                CreatorPublicUsername = _dbContext.WebAppUsers
                    .Where(wu => wu.WebAppId == a.Id)
                    .Join(_dbContext.Users, wu => wu.UserId, u => u.Id, (wu, u) => u)
                    .Select(u => u.PublicUsername)
                    .FirstOrDefault(),
                CreatorDisplayName = _dbContext.WebAppUsers
                    .Where(wu => wu.WebAppId == a.Id)
                    .Join(_dbContext.Users, wu => wu.UserId, u => u.Id, (wu, u) => u)
                    .Select(u => !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : u.Name)
                    .FirstOrDefault(),
                Summary = a.Summary,
                Description = a.Description,
                ThumbnailUrl = a.ThumbnailUrl ??
                    _dbContext.WebAppScreenshots
                        .Where(s => s.WebAppId == a.Id)
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.CreatedAt)
                        .Select(s => s.ImageUrl)
                        .FirstOrDefault(),
                Category = a.Category,
                Tags = a.Tags,
                ViewCount = a.ViewCount,
                StarCount = a.StarCount,
                IsStarredByCurrentUser = currentUserId.HasValue &&
                    _dbContext.WebAppStars.Any(s => s.WebAppId == a.Id && s.UserId == currentUserId.Value),
                PublicUrl = a.PublicUrl,
                PublishedAt = a.PublishedAt,
                IsFeatured = a.IsFeatured,
                Screenshots = _dbContext.WebAppScreenshots
                    .Where(s => s.WebAppId == a.Id)
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.CreatedAt)
                    .Select(s => new MarketplaceAppScreenshotDto
                    {
                        Id = s.Id,
                        ImageUrl = s.ImageUrl,
                        Caption = s.Caption,
                        SortOrder = s.SortOrder
                    })
                    .ToList()
            })
            .ToListAsync();

        return Ok(app);
    }

    [HttpGet("apps/{appId:guid}/creator-apps")]
    public async Task<ActionResult<List<MarketplaceAppCardDto>>> GetCreatorApps(Guid appId, [FromQuery] int take = 6)
    {
        var currentUserId = TryGetCurrentUserId();

        var creatorUserId = await _dbContext.WebApps
            .AsNoTracking()
            .Where(a =>
                a.Id == appId &&
                a.IsPublished &&
                a.MarketplaceStatus == MarketplaceAppStatuses.Approved)
            .Join(_dbContext.WebAppUsers, a => a.Id, wu => wu.WebAppId, (_, wu) => wu.UserId)
            .FirstOrDefaultAsync();

        if (creatorUserId == Guid.Empty)
        {
            return NotFound(new { message = "Creator not found for app" });
        }

        var apps = await _dbContext.WebApps
            .AsNoTracking()
            .Where(a =>
                a.Id != appId &&
                a.IsPublished &&
                a.MarketplaceStatus == MarketplaceAppStatuses.Approved &&
                _dbContext.WebAppUsers.Any(wu => wu.WebAppId == a.Id && wu.UserId == creatorUserId))
            .OrderByDescending(a => a.IsProfileFeatured)
            .ThenByDescending(a => a.IsFeatured)
            .ThenByDescending(a => a.PublishedAt)
            .Take(Math.Clamp(take, 1, 12))
            .Select(a => new MarketplaceAppCardDto
            {
                Id = a.Id,
                Name = a.Name,
                CreatorPublicUsername = _dbContext.Users
                    .Where(u => u.Id == creatorUserId)
                    .Select(u => u.PublicUsername)
                    .FirstOrDefault(),
                CreatorDisplayName = _dbContext.Users
                    .Where(u => u.Id == creatorUserId)
                    .Select(u => !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : u.Name)
                    .FirstOrDefault(),
                Summary = a.Summary,
                Description = a.Description,
                ThumbnailUrl = a.ThumbnailUrl ??
                    _dbContext.WebAppScreenshots
                        .Where(s => s.WebAppId == a.Id)
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.CreatedAt)
                        .Select(s => s.ImageUrl)
                        .FirstOrDefault(),
                Category = a.Category,
                Tags = a.Tags,
                ViewCount = a.ViewCount,
                StarCount = a.StarCount,
                IsStarredByCurrentUser = currentUserId.HasValue &&
                    _dbContext.WebAppStars.Any(s => s.WebAppId == a.Id && s.UserId == currentUserId.Value),
                PublicUrl = a.PublicUrl,
                PublishedAt = a.PublishedAt,
                IsFeatured = a.IsFeatured,
                Screenshots = _dbContext.WebAppScreenshots
                    .Where(s => s.WebAppId == a.Id)
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.CreatedAt)
                    .Select(s => new MarketplaceAppScreenshotDto
                    {
                        Id = s.Id,
                        ImageUrl = s.ImageUrl,
                        Caption = s.Caption,
                        SortOrder = s.SortOrder
                    })
                    .ToList()
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

public class MarketplaceAppCardDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CreatorPublicUsername { get; set; }
    public string? CreatorDisplayName { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public int ViewCount { get; set; }
    public int StarCount { get; set; }
    public bool IsStarredByCurrentUser { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public bool IsFeatured { get; set; }
    public List<MarketplaceAppScreenshotDto> Screenshots { get; set; } = new();
}

public class MarketplaceAppDetailsDto : MarketplaceAppCardDto
{
    public List<MarketplaceAppCardDto> RelatedApps { get; set; } = new();
}

public class MarketplaceAppScreenshotDto
{
    public Guid Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
}
