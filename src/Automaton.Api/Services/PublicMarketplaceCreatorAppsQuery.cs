using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class PublicMarketplaceCreatorAppsQuery(ApplicationDbContext dbContext)
{
    public async Task<Guid> FindCreatorUserIdAsync(Guid appId, CancellationToken cancellationToken = default)
        => await PublishedApps()
            .Where(app => app.Id == appId)
            .Join(dbContext.WebAppUsers, app => app.Id, owner => owner.WebAppId, (_, owner) => owner.UserId)
            .FirstOrDefaultAsync(cancellationToken);

    public IQueryable<MarketplaceAppCardDto> FindCreatorApps(
        Guid appId,
        Guid creatorUserId,
        Guid? currentUserId,
        int take)
        => PublishedApps()
            .Where(app =>
                app.Id != appId &&
                dbContext.WebAppUsers.Any(owner => owner.WebAppId == app.Id && owner.UserId == creatorUserId))
            .OrderByDescending(app => app.IsProfileFeatured)
            .ThenByDescending(app => app.IsFeatured)
            .ThenByDescending(app => app.PublishedAt)
            .Take(Math.Clamp(take, 1, 12))
            .Select(app => new MarketplaceAppCardDto
            {
                Id = app.Id,
                Name = app.Name,
                CreatorPublicUsername = dbContext.Users
                    .Where(user => user.Id == creatorUserId)
                    .Select(user => user.PublicUsername)
                    .FirstOrDefault(),
                CreatorDisplayName = dbContext.Users
                    .Where(user => user.Id == creatorUserId)
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
            });

    private IQueryable<WebApp> PublishedApps()
        => dbContext.WebApps
            .AsNoTracking()
            .Where(app =>
                app.CatalogVisibility == AppCatalogVisibilities.Listed &&
                app.ReviewStatus == AppReviewStatuses.Approved &&
                app.AccessStatus == AppAccessStatuses.Active);
}
