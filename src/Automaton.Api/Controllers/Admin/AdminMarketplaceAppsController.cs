using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/marketplace-apps")]
[Authorize(Policy = "Administrators")]
public class AdminMarketplaceAppsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly RecentActivityService _recentActivityService;
    private readonly PublishedAppStorageService _publishedAppStorageService;

    public AdminMarketplaceAppsController(
        ApplicationDbContext dbContext,
        RecentActivityService recentActivityService,
        PublishedAppStorageService publishedAppStorageService)
    {
        _dbContext = dbContext;
        _recentActivityService = recentActivityService;
        _publishedAppStorageService = publishedAppStorageService;
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
                StarCount = a.StarCount,
                IsFeatured = a.IsFeatured,
                Category = a.Category,
                Tags = a.Tags,
                ReviewStatus = a.ReviewStatus,
                AccessStatus = a.AccessStatus,
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

        var previousReviewStatus = app.ReviewStatus;
        var previousAccessStatus = app.AccessStatus;
        var previousReason = app.MarketplaceModerationReason;

        app.IsFeatured = request.IsFeatured;
        app.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        app.ReviewStatus = NormalizeReviewStatus(request.ReviewStatus);
        app.AccessStatus = NormalizeAccessStatus(request.AccessStatus);
        app.MarketplaceStatus = MapLegacyMarketplaceStatus(app.AccessStatus);
        app.MarketplaceModerationReason = string.IsNullOrWhiteSpace(request.MarketplaceModerationReason)
            ? null
            : request.MarketplaceModerationReason.Trim();
        app.MarketplaceModeratedAt = DateTime.UtcNow;
        app.MarketplaceModeratedByUserId = GetCurrentUserId();
        app.Updated = DateTime.UtcNow;

        var ownerUserId = await _dbContext.WebAppUsers
            .Where(link => link.WebAppId == app.Id)
            .OrderBy(link => link.UserId)
            .Select(link => (Guid?)link.UserId)
            .FirstOrDefaultAsync();

        if (ownerUserId.HasValue && app.IsPublished && !string.IsNullOrWhiteSpace(app.PublicUrl))
        {
            if (app.AccessStatus != AppAccessStatuses.Active)
            {
                await _publishedAppStorageService.UnpublishAsync(app.PublicUrl);
            }
            else
            {
                await _publishedAppStorageService.PublishAsync(
                    ownerUserId.Value,
                    app.Id,
                    new PublishedAppPackageMetadata(
                        app.Id,
                        app.PublicUrl,
                        app.Name,
                        app.Summary,
                        app.Description,
                        app.Version));
            }
        }

        await _dbContext.SaveChangesAsync();

        var moderationDescription = BuildModerationAuditDescription(
            app.Name,
            previousReviewStatus,
            app.ReviewStatus,
            previousAccessStatus,
            app.AccessStatus,
            previousReason,
            app.MarketplaceModerationReason);

        await _recentActivityService.LogActivityAsync(
            Automaton.Core.Enums.ActivityType.AppUpdated,
            app.Name,
            app.Id,
            moderationDescription);

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

    private static string NormalizeReviewStatus(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            AppReviewStatuses.Flagged => AppReviewStatuses.Flagged,
            _ => AppReviewStatuses.Approved,
        };
    }

    private static string NormalizeAccessStatus(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            AppAccessStatuses.Hidden => AppAccessStatuses.Hidden,
            AppAccessStatuses.Disabled => AppAccessStatuses.Disabled,
            _ => AppAccessStatuses.Active,
        };
    }

    private static string MapLegacyMarketplaceStatus(string accessStatus)
    {
        if (accessStatus == AppAccessStatuses.Disabled)
        {
            return MarketplaceAppStatuses.Banned;
        }

        if (accessStatus == AppAccessStatuses.Hidden)
        {
            return MarketplaceAppStatuses.Hidden;
        }

        return MarketplaceAppStatuses.Approved;
    }

    private static string Slugify(string value)
    {
        var slug = Regex.Replace(
            value.Trim().ToLowerInvariant(),
            @"[^a-z0-9]+",
            "-",
            RegexOptions.None,
            TimeSpan.FromSeconds(1)).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? Guid.NewGuid().ToString("N") : slug;
    }

    private static string BuildModerationAuditDescription(
        string appName,
        string previousReviewStatus,
        string newReviewStatus,
        string previousAccessStatus,
        string newAccessStatus,
        string? previousReason,
        string? newReason)
    {
        var changes = new List<string>();

        if (!string.Equals(previousReviewStatus, newReviewStatus, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"ReviewStatus {previousReviewStatus} -> {newReviewStatus}");
        }

        if (!string.Equals(previousAccessStatus, newAccessStatus, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"AccessStatus {previousAccessStatus} -> {newAccessStatus}");
        }

        if (!string.Equals(previousReason?.Trim(), newReason?.Trim(), StringComparison.Ordinal))
        {
            changes.Add($"Note: {(string.IsNullOrWhiteSpace(newReason) ? "cleared" : newReason.Trim())}");
        }

        if (changes.Count == 0)
        {
            changes.Add("Moderation fields saved without state changes");
        }

        return $"Admin updated moderation for '{appName}': {string.Join("; ", changes)}";
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
    public int StarCount { get; set; }
    public bool IsFeatured { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public string ReviewStatus { get; set; } = AppReviewStatuses.Approved;
    public string AccessStatus { get; set; } = AppAccessStatuses.Active;
    public string? MarketplaceModerationReason { get; set; }
    public DateTime? MarketplaceModeratedAt { get; set; }
    public Guid? MarketplaceModeratedByUserId { get; set; }
    public string? OwnerDisplayName { get; set; }
    public string? OwnerPublicUsername { get; set; }
}

public class UpdateMarketplaceAppRequest
{
    [JsonRequired]
    public bool IsFeatured { get; set; }
    public string? Category { get; set; }
    public string? ReviewStatus { get; set; }
    public string? AccessStatus { get; set; }
    public string? MarketplaceModerationReason { get; set; }
}
