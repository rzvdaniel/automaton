using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for fetching public apps and user profiles (no authentication required)
/// </summary>
public class PublicAppsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly PublishedAppsConfig publishedAppsConfig;
    private readonly ILogger<PublicAppsService> logger;

    public PublicAppsService(
        ApplicationDbContext dataContext,
        IOptions<PublishedAppsConfig> publishedAppsConfig,
        ILogger<PublicAppsService> logger)
    {
        this.dataContext = dataContext;
        this.publishedAppsConfig = publishedAppsConfig.Value;
        this.logger = logger;
    }

    /// <summary>
    /// Get public profile for a user by public username
    /// </summary>
    public async Task<PublicUserProfile?> GetPublicProfileAsync(string publicUsername)
    {
        var user = await dataContext.Users
            .FirstOrDefaultAsync(u => u.PublicUsername != null && u.PublicUsername.ToLower() == publicUsername.ToLower());

        if (user == null)
        {
            return null;
        }

        // Count public apps for this user
        var publicAppCount = await dataContext.WebAppUsers
            .Where(wu => wu.UserId == user.Id)
            .Join(dataContext.WebApps, wu => wu.WebAppId, w => w.Id, (wu, w) => w)
            .Where(w => w.IsPublished &&
                        w.ReviewStatus == AppReviewStatuses.Approved &&
                        w.AccessStatus == AppAccessStatuses.Active)
            .CountAsync();

        return new PublicUserProfile
        {
            Username = user.PublicUsername ?? string.Empty,
            DisplayName = !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Name,
            AvatarUrl = user.AvatarUrl,
            Bio = user.Bio,
            PublicAppCount = publicAppCount
        };
    }

    /// <summary>
    /// Get all public (published) apps for a user by public username
    /// </summary>
    public async Task<List<PublicAppCard>> GetPublicAppsAsync(string publicUsername, Guid? currentUserId = null)
    {
        var user = await dataContext.Users
            .FirstOrDefaultAsync(u => u.PublicUsername != null && u.PublicUsername.ToLower() == publicUsername.ToLower());

        if (user == null)
        {
            return new List<PublicAppCard>();
        }

        var apps = await dataContext.WebAppUsers
            .Where(wu => wu.UserId == user.Id)
            .Join(dataContext.WebApps, wu => wu.WebAppId, w => w.Id, (wu, w) => w)
            .Where(w => w.IsPublished &&
                        w.ReviewStatus == AppReviewStatuses.Approved &&
                        w.AccessStatus == AppAccessStatuses.Active)
            .OrderByDescending(w => w.IsProfileFeatured)
            .ThenByDescending(w => w.StarCount)
            .ThenByDescending(w => w.ViewCount)
            .ThenByDescending(w => w.PublishedAt)
            .Select(w => new
            {
                w.Id,
                w.Name,
                w.Summary,
                w.Description,
                ThumbnailUrl = w.ThumbnailUrl ??
                    dataContext.WebAppScreenshots
                        .Where(s => s.WebAppId == w.Id)
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.CreatedAt)
                        .Select(s => s.ImageUrl)
                        .FirstOrDefault(),
                w.Category,
                w.PublishedAt,
                w.PublicUrl,
                w.ViewCount,
                w.IsFeatured,
                w.IsProfileFeatured,
                w.StarCount,
                IsStarredByCurrentUser = currentUserId.HasValue &&
                    dataContext.WebAppStars.Any(s => s.WebAppId == w.Id && s.UserId == currentUserId.Value),
                Screenshots = dataContext.WebAppScreenshots
                    .Where(s => s.WebAppId == w.Id)
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.CreatedAt)
                    .Select(s => new PublicAppScreenshot
                    {
                        Id = s.Id,
                        ImageUrl = s.ImageUrl,
                        Caption = s.Caption,
                        SortOrder = s.SortOrder
                    })
                    .ToList()
            })
            .ToListAsync();

        return apps.Select(app => new PublicAppCard
        {
            Id = app.Id,
            Name = app.Name,
            Summary = app.Summary,
            Description = app.Description,
            ThumbnailUrl = app.ThumbnailUrl,
            Category = app.Category,
            PublishedAt = app.PublishedAt,
            AppUrl = BuildPublicAppUrl(app.PublicUrl),
            PublicUrl = app.PublicUrl,
            ViewCount = app.ViewCount,
            IsFeatured = app.IsFeatured,
            IsProfileFeatured = app.IsProfileFeatured,
            StarCount = app.StarCount,
            IsStarredByCurrentUser = app.IsStarredByCurrentUser,
            Screenshots = app.Screenshots
        }).ToList();
    }

    /// <summary>
    /// Get details of a specific public app
    /// </summary>
    public async Task<PublicAppDetails?> GetPublicAppDetailsAsync(string publicUsername, Guid appId, Guid? currentUserId = null)
    {
        var user = await dataContext.Users
            .FirstOrDefaultAsync(u => u.PublicUsername != null && u.PublicUsername.ToLower() == publicUsername.ToLower());

        if (user == null)
        {
            return null;
        }

        var app = await dataContext.WebAppUsers
            .Where(wu => wu.UserId == user.Id && wu.WebAppId == appId)
            .Join(dataContext.WebApps, wu => wu.WebAppId, w => w.Id, (wu, w) => w)
            .Where(w => w.IsPublished &&
                        w.ReviewStatus == AppReviewStatuses.Approved &&
                        w.AccessStatus == AppAccessStatuses.Active)
            .Select(w => new
            {
                w.Id,
                w.Name,
                w.Summary,
                w.Description,
                ThumbnailUrl = w.ThumbnailUrl ??
                    dataContext.WebAppScreenshots
                        .Where(s => s.WebAppId == w.Id)
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.CreatedAt)
                        .Select(s => s.ImageUrl)
                        .FirstOrDefault(),
                w.Category,
                w.Tags,
                w.PublishedAt,
                w.PublicUrl,
                w.ViewCount,
                w.IsFeatured,
                w.IsProfileFeatured,
                w.StarCount,
                IsStarredByCurrentUser = currentUserId.HasValue &&
                    dataContext.WebAppStars.Any(s => s.WebAppId == w.Id && s.UserId == currentUserId.Value)
            })
            .FirstOrDefaultAsync();

        if (app != null)
        {
            var screenshots = await dataContext.WebAppScreenshots
                .Where(s => s.WebAppId == app.Id)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.CreatedAt)
                .Select(s => new PublicAppScreenshot
                {
                    Id = s.Id,
                    ImageUrl = s.ImageUrl,
                    Caption = s.Caption,
                    SortOrder = s.SortOrder
                })
                .ToListAsync();

            return new PublicAppDetails
            {
                Id = app.Id,
                Name = app.Name,
                Summary = app.Summary,
                Description = app.Description,
                ThumbnailUrl = app.ThumbnailUrl,
                Category = app.Category,
                Tags = app.Tags,
                PublishedAt = app.PublishedAt,
                AppUrl = BuildPublicAppUrl(app.PublicUrl),
                PublicUrl = app.PublicUrl,
                ViewCount = app.ViewCount,
                IsFeatured = app.IsFeatured,
                IsProfileFeatured = app.IsProfileFeatured,
                StarCount = app.StarCount,
                IsStarredByCurrentUser = app.IsStarredByCurrentUser,
                Screenshots = screenshots
            };
        }

        return null;
    }

    /// <summary>
    /// Check if a username exists
    /// </summary>
    public async Task<bool> UsernameExistsAsync(string username)
    {
        return await dataContext.Users
            .AnyAsync(u => u.UserName != null && u.UserName.ToLower() == username.ToLower());
    }

    private string BuildPublicAppUrl(string? slug)
    {
        return string.IsNullOrWhiteSpace(slug)
            ? string.Empty
            : PublishedAppStorageService.BuildPublicAppUrl(publishedAppsConfig.PublicBaseUrl, slug);
    }
}

// DTOs for public apps feature

public class PublicUserProfile
{
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public int PublicAppCount { get; set; }
}

public class PublicAppCard
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Category { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string AppUrl { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public int ViewCount { get; set; }
    public int StarCount { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsProfileFeatured { get; set; }
    public bool IsStarredByCurrentUser { get; set; }
    public List<PublicAppScreenshot> Screenshots { get; set; } = new();
}

public class PublicAppDetails : PublicAppCard
{
    public string? Tags { get; set; }
}

public class PublicAppScreenshot
{
    public Guid Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
}

public class PublicAppsResponse
{
    public PublicUserProfile Profile { get; set; } = new();
    public List<PublicAppCard> Apps { get; set; } = new();
}
