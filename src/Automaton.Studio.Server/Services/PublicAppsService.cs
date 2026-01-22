using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for fetching public apps and user profiles (no authentication required)
/// </summary>
public class PublicAppsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly ILogger<PublicAppsService> logger;

    public PublicAppsService(
        ApplicationDbContext dataContext,
        ILogger<PublicAppsService> logger)
    {
        this.dataContext = dataContext;
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
            .Where(w => w.IsPublished)
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
    public async Task<List<PublicAppCard>> GetPublicAppsAsync(string publicUsername)
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
            .Where(w => w.IsPublished)
            .OrderByDescending(w => w.IsFeatured)
            .ThenByDescending(w => w.PublishedAt)
            .Select(w => new PublicAppCard
            {
                Id = w.Id,
                Name = w.Name,
                DisplayName = w.DisplayName,
                Description = w.Description,
                ThumbnailUrl = w.ThumbnailUrl,
                Category = w.Category,
                PublishedAt = w.PublishedAt,
                AppUrl = $"/web/{w.PublicUrl}",
                PublicUrl = w.PublicUrl,
                ViewCount = w.ViewCount,
                IsFeatured = w.IsFeatured
            })
            .ToListAsync();

        return apps;
    }

    /// <summary>
    /// Get details of a specific public app
    /// </summary>
    public async Task<PublicAppDetails?> GetPublicAppDetailsAsync(string publicUsername, Guid appId)
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
            .Where(w => w.IsPublished)
            .Select(w => new PublicAppDetails
            {
                Id = w.Id,
                Name = w.Name,
                DisplayName = w.DisplayName,
                Description = w.Description,
                ThumbnailUrl = w.ThumbnailUrl,
                Category = w.Category,
                Tags = w.Tags,
                PublishedAt = w.PublishedAt,
                AppUrl = $"/web/{w.PublicUrl}",
                PublicUrl = w.PublicUrl,
                ViewCount = w.ViewCount,
                IsFeatured = w.IsFeatured
            })
            .FirstOrDefaultAsync();

        return app;
    }

    /// <summary>
    /// Check if a username exists
    /// </summary>
    public async Task<bool> UsernameExistsAsync(string username)
    {
        return await dataContext.Users
            .AnyAsync(u => u.UserName != null && u.UserName.ToLower() == username.ToLower());
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
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Category { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string AppUrl { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public int ViewCount { get; set; }
    public bool IsFeatured { get; set; }
}

public class PublicAppDetails : PublicAppCard
{
    public string? Tags { get; set; }
}

public class PublicAppsResponse
{
    public PublicUserProfile Profile { get; set; } = new();
    public List<PublicAppCard> Apps { get; set; } = new();
}
