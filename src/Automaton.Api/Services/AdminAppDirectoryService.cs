using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed record AdminAppListItem(
    Guid Id,
    string Name,
    string? PublicUrl,
    bool IsPublished,
    string AccessStatus,
    DateTime Updated,
    Guid? OwnerUserId,
    string OwnerName,
    string OwnerEmail,
    IReadOnlyList<string> WaitingPermissions);

public sealed record AdminAppListResponse(
    IReadOnlyList<AdminAppListItem> Items,
    int Total,
    int WaitingForApprovalTotal);

/// <summary>
/// Every app on the server for system administrators, with its owner and any permission requests that
/// wait for a decision, so administrators reach apps without going through each user.
/// </summary>
public sealed class AdminAppDirectoryService(ApplicationDbContext db, AdminAuthorizationService admin)
{
    public const int MaxPageSize = 100;

    public async Task<AdminAppListResponse> ListAsync(string? search, bool waitingOnly, int page, int pageSize,
        CancellationToken token)
    {
        admin.EnsureAdmin();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var waiting = await WaitingByAppAsync(token);
        var apps = Search(search);
        if (waitingOnly)
        {
            var ids = waiting.Keys.ToArray();
            apps = apps.Where(app => ids.Contains(app.Id));
        }

        var total = await apps.CountAsync(token);
        var rows = await apps.OrderByDescending(app => app.Updated)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(app => new
            {
                app.Id, app.Name, app.PublicUrl, app.IsPublished, app.AccessStatus, app.Updated,
                OwnerUserId = app.WebAppUsers.OrderBy(link => link.UserId).Select(link => (Guid?)link.UserId).FirstOrDefault()
            })
            .ToListAsync(token);

        var ownerKeys = rows.Where(row => row.OwnerUserId.HasValue).Select(row => row.OwnerUserId!.Value).Distinct().ToArray();
        var owners = await OwnersAsync(ownerKeys, token);

        var items = rows.Select(row =>
        {
            var owner = row.OwnerUserId is { } ownerId ? owners.GetValueOrDefault(ownerId) : default;
            return new AdminAppListItem(row.Id, row.Name, row.PublicUrl, row.IsPublished, row.AccessStatus, row.Updated,
                row.OwnerUserId, owner.Name ?? string.Empty, owner.Email ?? string.Empty,
                waiting.GetValueOrDefault(row.Id) ?? []);
        }).ToList();
        return new AdminAppListResponse(items, total, waiting.Count);
    }

    private IQueryable<WebApp> Search(string? search)
    {
        var apps = db.WebApps.AsNoTracking();
        var query = search?.Trim();
        if (string.IsNullOrEmpty(query)) return apps;
        var ownerIds = db.Users.Where(user => user.Email!.Contains(query) || user.Name.Contains(query)
                || (user.DisplayName != null && user.DisplayName.Contains(query)))
            .Select(user => user.Id);
        return apps.Where(app => app.Name.Contains(query) || (app.PublicUrl != null && app.PublicUrl.Contains(query))
            || app.WebAppUsers.Any(link => ownerIds.Contains(link.UserId)));
    }

    private async Task<Dictionary<Guid, (string? Name, string? Email)>> OwnersAsync(Guid[] ids, CancellationToken token)
    {
        var users = await db.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
            .Select(user => new { user.Id, user.Name, user.DisplayName, user.Email })
            .ToListAsync(token);
        return users.ToDictionary(user => user.Id,
            user => ((string?)(string.IsNullOrWhiteSpace(user.DisplayName) ? user.Name : user.DisplayName), user.Email));
    }

    /// <summary>Apps with at least one requested permission no administrator has decided yet.</summary>
    public async Task<Dictionary<Guid, IReadOnlyList<string>>> WaitingByAppAsync(CancellationToken token)
    {
        // Only apps whose stored contract mentions a supported permission can request one.
        var candidates = await db.WebApps.AsNoTracking()
            .Where(app => app.ApplicationContractJson.Contains(ApplicationContractValidator.SupportedPermission))
            .Select(app => new { app.Id, app.ApplicationContractJson })
            .ToListAsync(token);
        if (candidates.Count == 0) return [];

        var ids = candidates.Select(app => app.Id).ToArray();
        var decided = await db.AppPermissionApprovals.AsNoTracking()
            .Where(approval => ids.Contains(approval.AppId) && approval.Status != AppPermissionStatuses.Pending)
            .Select(approval => new { approval.AppId, approval.Permission })
            .ToListAsync(token);
        var decidedSet = decided.Select(item => (item.AppId, item.Permission)).ToHashSet();

        var result = new Dictionary<Guid, IReadOnlyList<string>>();
        foreach (var app in candidates)
        {
            var waiting = AppPermissionService.RequestedPermissions(app.ApplicationContractJson)
                .Where(permission => !decidedSet.Contains((app.Id, permission))).ToArray();
            if (waiting.Length > 0) result[app.Id] = waiting;
        }
        return result;
    }
}
