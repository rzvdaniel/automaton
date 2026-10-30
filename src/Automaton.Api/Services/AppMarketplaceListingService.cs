using Automaton.Api.Data;
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
    RecentActivityService recentActivityService)
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

        var invalid = ValidateText(request);
        if (invalid is not null)
            return AppListingUpdateResult.Invalid(invalid);

        string? category = null;
        if (request.Category is not null)
        {
            var resolved = await ResolveCategoryAsync(request.Category, cancellationToken);
            if (!resolved.Found)
                return AppListingUpdateResult.Invalid(resolved.Error!);
            category = resolved.Name;
        }

        string? tags = null;
        if (request.Tags is not null)
        {
            try { tags = AppTagNormalizer.Serialize(request.Tags); }
            catch (AppTagValidationException exception) { return AppListingUpdateResult.Invalid(exception.Message); }
        }

        if (request.Summary is not null) app.Summary = request.Summary.Trim();
        if (request.Description is not null) app.Description = request.Description.Trim();
        if (request.Category is not null) app.Category = category;
        if (request.Tags is not null) app.Tags = tags;
        app.Updated = DateTime.UtcNow;

        await dataContext.SaveChangesAsync(cancellationToken);
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUpdated, app.Name, appId, cancellationToken);
        return AppListingUpdateResult.Updated();
    }

    private static string? ValidateText(UpdateAppListingRequest request)
    {
        if (request.Summary is not null && request.Summary.Trim().Length > MaximumSummaryLength)
            return $"The summary cannot exceed {MaximumSummaryLength} characters.";
        if (request.Description is not null && request.Description.Trim().Length > MaximumDescriptionLength)
            return $"The description cannot exceed {MaximumDescriptionLength} characters.";
        return null;
    }

    // An empty category clears it; otherwise it must match a marketplace category by name or slug.
    private async Task<(bool Found, string? Name, string? Error)> ResolveCategoryAsync(
        string requested,
        CancellationToken cancellationToken)
    {
        var value = requested.Trim();
        if (value.Length == 0)
            return (true, null, null);

        var categories = await dataContext.MarketplaceCategories
            .OrderBy(category => category.SortOrder)
            .Select(category => new { category.Name, category.Slug })
            .ToListAsync(cancellationToken);
        var match = categories.FirstOrDefault(category =>
            string.Equals(category.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category.Slug, value, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return (true, match.Name, null);

        var available = categories.Count == 0 ? "none" : string.Join(", ", categories.Select(category => category.Name));
        return (false, null, $"Unknown category '{value}'. Available categories: {available}.");
    }
}

public sealed record UpdateAppListingRequest(
    string? Summary = null,
    string? Description = null,
    string? Category = null,
    IReadOnlyList<string>? Tags = null);

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
