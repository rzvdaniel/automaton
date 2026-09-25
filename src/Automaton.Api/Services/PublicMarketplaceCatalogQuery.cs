using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class PublicMarketplaceCatalogQuery(ApplicationDbContext dbContext)
{
    public async Task<List<MarketplaceAppCardDto>> GetAppsAsync(
        string? category,
        string? search,
        int take,
        Guid? currentUserId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.WebApps
            .AsNoTracking()
            .Where(app =>
                app.CatalogVisibility == AppCatalogVisibilities.Listed &&
                app.ReviewStatus == AppReviewStatuses.Approved &&
                app.AccessStatus == AppAccessStatuses.Active);

        query = ApplyCatalogFilters(query, category, search);

        return await query
            .OrderByDescending(app => app.IsFeatured)
            .ThenByDescending(app => app.StarCount)
            .ThenByDescending(app => app.ViewCount)
            .ThenByDescending(app => app.PublishedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(app => new MarketplaceAppCardDto
            {
                Id = app.Id,
                Name = app.Name,
                CreatorPublicUsername = dbContext.WebAppUsers
                    .Where(owner => owner.WebAppId == app.Id)
                    .Join(dbContext.Users, owner => owner.UserId, user => user.Id, (_, user) => user)
                    .Select(user => user.PublicUsername)
                    .FirstOrDefault(),
                CreatorDisplayName = dbContext.WebAppUsers
                    .Where(owner => owner.WebAppId == app.Id)
                    .Join(dbContext.Users, owner => owner.UserId, user => user.Id, (_, user) => user)
                    .Select(user => !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Name)
                    .FirstOrDefault(),
                Summary = app.Summary,
                Description = app.Description,
                ThumbnailUrl = app.ThumbnailUrl ?? dbContext.WebAppScreenshots
                    .Where(screenshot => screenshot.WebAppId == app.Id)
                    .OrderBy(screenshot => screenshot.SortOrder)
                    .ThenBy(screenshot => screenshot.CreatedAt)
                    .Select(screenshot => screenshot.ImageUrl)
                    .FirstOrDefault(),
                Category = app.Category,
                Tags = app.Tags,
                ViewCount = app.ViewCount,
                StarCount = app.StarCount,
                ForkCount = dbContext.WebApps.Count(fork => fork.ForkedFromAppId == app.Id),
                IsForkable = app.IsForkable,
                IsStarredByCurrentUser = currentUserId.HasValue &&
                    dbContext.WebAppStars.Any(star => star.WebAppId == app.Id && star.UserId == currentUserId.Value),
                PublicUrl = app.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    app.MarketplacePreviewMode == MarketplacePreviewModes.Launchable ? app.PublicUrl : null,
                CanLaunch = app.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    app.MarketplacePreviewMode == MarketplacePreviewModes.Launchable,
                PublishedAt = app.PublishedAt,
                UpdatedAt = app.Updated,
                IsFeatured = app.IsFeatured,
                Screenshots = dbContext.WebAppScreenshots
                    .Where(screenshot => screenshot.WebAppId == app.Id)
                    .OrderBy(screenshot => screenshot.SortOrder)
                    .ThenBy(screenshot => screenshot.CreatedAt)
                    .Select(screenshot => new MarketplaceAppScreenshotDto
                    {
                        Id = screenshot.Id,
                        ImageUrl = screenshot.ImageUrl,
                        Caption = screenshot.Caption,
                        SortOrder = screenshot.SortOrder
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<WebApp> ApplyCatalogFilters(
        IQueryable<WebApp> query,
        string? category,
        string? search)
    {
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(app => app.Category == category);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(app =>
                app.Name.ToLower().Contains(term) ||
                (app.Summary != null && app.Summary.ToLower().Contains(term)) ||
                (app.Tags != null && app.Tags.ToLower().Contains(term)));
        }
        return query;
    }
}
