using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Updates how an app is presented in the public marketplace: summary, description,
/// category and tags. Only the fields present in the request change.
/// </summary>
public sealed class AppMarketplaceListingService(
    ApplicationDbContext dataContext,
    ResourceAuthorizationService authorizationService,
    UserContextService userContextService,
    RecentActivityService recentActivityService,
    IAuditEventService auditEventService)
{
    public const int MaximumSummaryLength = 200;
    public const int MaximumDescriptionLength = 500;

    public async Task<AppListingUpdateResult> UpdateAsync(
        Guid appId,
        UpdateAppListingRequest request,
        CancellationToken cancellationToken = default)
    {
        var app = await authorizationService.GetAccessibleAppsQuery()
            .SingleOrDefaultAsync(item => item.Id == appId, cancellationToken);
        if (app is null)
            return AppListingUpdateResult.NotFound();
        if (!await authorizationService.CanUpdateAppAsync(appId, cancellationToken))
            return AppListingUpdateResult.Forbidden();

        var resolved = await ResolveAsync(request, cancellationToken);
        if (resolved.Error is not null)
            return AppListingUpdateResult.Invalid(resolved.Error);

        if (!Apply(app, request, resolved))
            return AppListingUpdateResult.Updated();

        app.Updated = DateTime.UtcNow;
        await dataContext.SaveChangesAsync(cancellationToken);
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUpdated, app.Name, appId, cancellationToken);
        await WriteAuditAsync(app, cancellationToken);
        return AppListingUpdateResult.Updated();
    }

    private async Task<ResolvedListing> ResolveAsync(UpdateAppListingRequest request, CancellationToken cancellationToken)
    {
        if (request.Summary is not null && request.Summary.Trim().Length > MaximumSummaryLength)
            return ResolvedListing.Invalid($"The summary cannot exceed {MaximumSummaryLength} characters.");
        if (request.Description is not null && request.Description.Trim().Length > MaximumDescriptionLength)
            return ResolvedListing.Invalid($"The description cannot exceed {MaximumDescriptionLength} characters.");

        string? category = null;
        if (request.Category is not null)
        {
            var match = await ResolveCategoryAsync(request.Category, cancellationToken);
            if (match.Error is not null)
                return ResolvedListing.Invalid(match.Error);
            category = match.Name;
        }

        try
        {
            var tags = request.Tags is null ? null : AppTagNormalizer.Serialize(request.Tags);
            return new ResolvedListing(category, tags, null);
        }
        catch (AppTagValidationException exception)
        {
            return ResolvedListing.Invalid(exception.Message);
        }
    }

    // Returns whether anything actually changed.
    private static bool Apply(WebApp app, UpdateAppListingRequest request, ResolvedListing resolved)
    {
        var before = (app.Summary, app.Description, app.Category, app.Tags);
        if (request.Summary is not null) app.Summary = request.Summary.Trim();
        if (request.Description is not null) app.Description = request.Description.Trim();
        if (request.Category is not null) app.Category = resolved.Category;
        if (request.Tags is not null) app.Tags = resolved.Tags;
        return before != (app.Summary, app.Description, app.Category, app.Tags);
    }

    // Matches Studio's save path, which records the same event for metadata changes.
    private Task WriteAuditAsync(WebApp app, CancellationToken cancellationToken)
        => auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Updated,
            EntityType = AuditEntityTypes.App,
            EntityId = app.Id,
            EventType = AuditEventTypes.AppMetadataUpdated,
            Message = $"Marketplace listing was updated for '{app.Name}'",
            Outcome = AuditOutcomes.Success,
            ActorUserId = userContextService.GetUserId(),
            Metadata = new { app.Name, app.Summary, app.Description, app.Category, app.Tags }
        }, cancellationToken);

    // An empty category clears it; otherwise it must match an active category by name or slug.
    private async Task<(string? Name, string? Error)> ResolveCategoryAsync(string requested, CancellationToken cancellationToken)
    {
        var value = requested.Trim();
        if (value.Length == 0)
            return (null, null);

        var categories = await dataContext.MarketplaceCategories
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .Select(category => new { category.Name, category.Slug })
            .ToListAsync(cancellationToken);
        var match = categories.FirstOrDefault(category =>
            string.Equals(category.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category.Slug, value, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return (match.Name, null);

        var available = categories.Count == 0 ? "none" : string.Join(", ", categories.Select(category => category.Name));
        return (null, $"Unknown category '{value}'. Available categories: {available}.");
    }

    private sealed record ResolvedListing(string? Category, string? Tags, string? Error)
    {
        public static ResolvedListing Invalid(string error) => new(null, null, error);
    }
}

public enum AppListingUpdateStatus
{
    Updated,
    NotFound,
    Forbidden,
    Invalid
}

public sealed record AppListingUpdateResult(AppListingUpdateStatus Status, string? Error = null)
{
    public static AppListingUpdateResult Updated() => new(AppListingUpdateStatus.Updated);
    public static AppListingUpdateResult NotFound() => new(AppListingUpdateStatus.NotFound);
    public static AppListingUpdateResult Forbidden() => new(AppListingUpdateStatus.Forbidden);
    public static AppListingUpdateResult Invalid(string error) => new(AppListingUpdateStatus.Invalid, error);
}
