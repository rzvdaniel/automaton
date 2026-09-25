using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/public/marketplace")]
[AllowAnonymous]
public class PublicMarketplaceController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly AppFileStorageService _appFileStorageService;
    private readonly AppsService _appsService;
    private readonly PublicMarketplaceCreatorAppsQuery _creatorAppsQuery;
    private readonly PublicMarketplaceCatalogQuery _catalogQuery;

    public PublicMarketplaceController(
        ApplicationDbContext dbContext,
        AppFileStorageService appFileStorageService,
        AppsService appsService,
        PublicMarketplaceCreatorAppsQuery creatorAppsQuery,
        PublicMarketplaceCatalogQuery catalogQuery)
    {
        _dbContext = dbContext;
        _appFileStorageService = appFileStorageService;
        _appsService = appsService;
        _creatorAppsQuery = creatorAppsQuery;
        _catalogQuery = catalogQuery;
    }

    [HttpGet("apps")]
    public async Task<ActionResult<List<MarketplaceAppCardDto>>> GetApps(
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] int take = 20)
    {
        var apps = await _catalogQuery.GetAppsAsync(
            category, search, take, TryGetCurrentUserId(), HttpContext.RequestAborted);

        await PopulateApplicationSourceFlagsAsync(apps);

        return Ok(apps);
    }

    [HttpGet("apps/{appId:guid}")]
    public async Task<ActionResult<MarketplaceAppDetailsDto>> GetAppDetails(Guid appId)
    {
        var currentUserId = TryGetCurrentUserId();

        var app = await LoadAppDetailsAsync(appId, currentUserId);

        if (app == null)
        {
            return NotFound(new { message = "App not found or not published" });
        }

        var ownerUserId = await _dbContext.WebAppUsers
            .AsNoTracking()
            .Where(wu => wu.WebAppId == app.Id)
            .Select(wu => (Guid?)wu.UserId)
            .FirstOrDefaultAsync();

        app.CanRemix = app.IsForkable &&
            ownerUserId.HasValue &&
            _appFileStorageService.AppSourceExists(ownerUserId.Value, app.Id);

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

        await PopulateApplicationSourceFlagsAsync([app]);

        app.RelatedApps = await LoadRelatedAppsAsync(app, currentUserId);

        await PopulateApplicationSourceFlagsAsync(app.RelatedApps);

        app.ForksAndRemixes = await LoadForksAndRemixesAsync(app, currentUserId);

        await PopulateApplicationSourceFlagsAsync(app.ForksAndRemixes);

        return Ok(app);
    }

    private async Task<MarketplaceAppDetailsDto?> LoadAppDetailsAsync(Guid appId, Guid? currentUserId)
    {
        return await _dbContext.WebApps
            .AsNoTracking()
            .Where(a =>
                a.Id == appId &&
                a.CatalogVisibility == AppCatalogVisibilities.Listed &&
                a.ReviewStatus == AppReviewStatuses.Approved &&
                a.AccessStatus == AppAccessStatuses.Active)
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
                ForkCount = _dbContext.WebApps.Count(f => f.ForkedFromAppId == a.Id),
                IsForkable = a.IsForkable,
                IsStarredByCurrentUser = currentUserId.HasValue &&
                    _dbContext.WebAppStars.Any(s => s.WebAppId == a.Id && s.UserId == currentUserId.Value),
                PublicUrl = a.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    a.MarketplacePreviewMode == MarketplacePreviewModes.Launchable ? a.PublicUrl : null,
                CanLaunch = a.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    a.MarketplacePreviewMode == MarketplacePreviewModes.Launchable,
                PublishedAt = a.PublishedAt,
                UpdatedAt = a.Updated,
                IsFeatured = a.IsFeatured
            })
            .FirstOrDefaultAsync();
    }

    private async Task<List<MarketplaceAppCardDto>> LoadRelatedAppsAsync(MarketplaceAppDetailsDto app, Guid? currentUserId)
    {
        return await _dbContext.WebApps
            .AsNoTracking()
            .Where(a =>
                a.Id != app.Id &&
                a.CatalogVisibility == AppCatalogVisibilities.Listed &&
                a.ReviewStatus == AppReviewStatuses.Approved &&
                a.AccessStatus == AppAccessStatuses.Active &&
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
                ForkCount = _dbContext.WebApps.Count(f => f.ForkedFromAppId == a.Id),
                IsForkable = a.IsForkable,
                IsStarredByCurrentUser = currentUserId.HasValue &&
                    _dbContext.WebAppStars.Any(s => s.WebAppId == a.Id && s.UserId == currentUserId.Value),
                PublicUrl = a.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    a.MarketplacePreviewMode == MarketplacePreviewModes.Launchable ? a.PublicUrl : null,
                CanLaunch = a.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    a.MarketplacePreviewMode == MarketplacePreviewModes.Launchable,
                PublishedAt = a.PublishedAt,
                UpdatedAt = a.Updated,
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
    }

    private async Task<List<MarketplaceAppCardDto>> LoadForksAndRemixesAsync(MarketplaceAppDetailsDto app, Guid? currentUserId)
    {
        return await _dbContext.WebApps
            .AsNoTracking()
            .Where(a =>
                a.ForkedFromAppId == app.Id &&
                a.CatalogVisibility == AppCatalogVisibilities.Listed &&
                a.ReviewStatus == AppReviewStatuses.Approved &&
                a.AccessStatus == AppAccessStatuses.Active)
            .OrderByDescending(a => a.PublishedAt)
            .Take(6)
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
                ForkCount = _dbContext.WebApps.Count(f => f.ForkedFromAppId == a.Id),
                IsForkable = a.IsForkable,
                IsStarredByCurrentUser = currentUserId.HasValue &&
                    _dbContext.WebAppStars.Any(s => s.WebAppId == a.Id && s.UserId == currentUserId.Value),
                PublicUrl = a.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    a.MarketplacePreviewMode == MarketplacePreviewModes.Launchable ? a.PublicUrl : null,
                CanLaunch = a.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    a.MarketplacePreviewMode == MarketplacePreviewModes.Launchable,
                PublishedAt = a.PublishedAt,
                UpdatedAt = a.Updated,
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
    }

    [HttpGet("apps/{appId:guid}/creator-apps")]
    public async Task<ActionResult<List<MarketplaceAppCardDto>>> GetCreatorApps(Guid appId, [FromQuery] int take = 6)
    {
        var currentUserId = TryGetCurrentUserId();

        var creatorUserId = await _creatorAppsQuery.FindCreatorUserIdAsync(appId);

        if (creatorUserId == Guid.Empty)
        {
            return NotFound(new { message = "Creator not found for app" });
        }

        var apps = await _creatorAppsQuery
            .FindCreatorApps(appId, creatorUserId, currentUserId, take)
            .ToListAsync();

        await PopulateApplicationSourceFlagsAsync(apps);

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

    [HttpPost("apps/{appId:guid}/launch")]
    [EnableRateLimiting(RateLimitPolicyNames.PublicMarketplaceAppLaunch)]
    public async Task<IActionResult> TrackAppLaunch(Guid appId)
    {
        var appExists = await _dbContext.WebApps
            .AsNoTracking()
            .AnyAsync(a =>
                a.Id == appId &&
                a.CatalogVisibility == AppCatalogVisibilities.Listed &&
                a.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                a.MarketplacePreviewMode == MarketplacePreviewModes.Launchable &&
                a.ReviewStatus == AppReviewStatuses.Approved &&
                a.AccessStatus == AppAccessStatuses.Active);

        if (!appExists)
        {
            return NotFound(new { message = "App not found or not published" });
        }

        try
        {
            await _appsService.IncrementViewCountAsync(appId);
        }
        catch (Exception ex)
        {
            // Analytics should not break launching.
            // If controller already has ILogger, use logger instead.
            Serilog.Log.Warning(ex, "Failed to track app launch for {AppId}", appId);
        }

        return NoContent();
    }

    private Guid? TryGetCurrentUserId()
    {
        var userIdString = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdString, out var userId) ? userId : null;
    }

    private async Task PopulateApplicationSourceFlagsAsync(IEnumerable<MarketplaceAppCardDto> apps)
    {
        var appList = apps.ToList();
        if (appList.Count == 0)
        {
            return;
        }

        var ownerIdsByAppId = await _dbContext.WebAppUsers
            .AsNoTracking()
            .Where(wu => appList.Select(app => app.Id).Contains(wu.WebAppId))
            .GroupBy(wu => wu.WebAppId)
            .Select(group => new
            {
                AppId = group.Key,
                OwnerUserId = group.Select(wu => wu.UserId).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.AppId, x => x.OwnerUserId);

        foreach (var app in appList)
        {
            app.HasApplicationSource = ownerIdsByAppId.TryGetValue(app.Id, out var ownerUserId) &&
                ownerUserId != Guid.Empty &&
                _appFileStorageService.HasAnySourceEntries(ownerUserId, app.Id);
        }
    }
}
