using Automaton.Api.Constants;
using Automaton.Api.Enums;
using System.Security.Cryptography;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed record AppPermissionStatus(string Permission, bool Approved, string Status);

/// <summary>System administrator approval and scoped credentials for app capabilities.</summary>
public sealed class AppPermissionService(
    ApplicationDbContext db,
    ISecretProtector protector,
    IAuditEventService? audit = null)
{
    public async Task<IReadOnlyList<AppPermissionStatus>> GetStatusAsync(Guid appId, CancellationToken token)
    {
        var requested = await RequestedAsync(appId, token);
        var approvals = await db.AppPermissionApprovals.Where(x => x.AppId == appId)
            .ToDictionaryAsync(x => x.Permission, token);
        return requested.Select(permission =>
        {
            var found = approvals.TryGetValue(permission, out var approval);
            var status = found ? approval!.Status : AppPermissionStatuses.Pending;
            return new AppPermissionStatus(permission, found && approval!.ProtectedKey is not null, status);
        }).ToArray();
    }

    public async Task<IReadOnlyList<AppPermissionStatus>> SetApprovalAsync(Guid appId, string permission, bool approved,
        CancellationToken token)
    {
        if (permission != AppPermissionApproval.BackupsRead) throw new ArgumentException("Unsupported app permission.", nameof(permission));
        var requested = await RequestedAsync(appId, token);
        if (approved && !requested.Contains(permission, StringComparer.Ordinal))
            throw new InvalidOperationException("The app has not requested this permission.");

        var row = await db.AppPermissionApprovals.SingleOrDefaultAsync(x => x.AppId == appId && x.Permission == permission, token);
        if (row is null)
        {
            row = new AppPermissionApproval { Id = Guid.NewGuid(), AppId = appId, Permission = permission };
            db.AppPermissionApprovals.Add(row);
        }

        if (!approved)
        {
            row.Status = row.ProtectedKey is null ? AppPermissionStatuses.Rejected : AppPermissionStatuses.Revoked;
            row.ProtectedKey = null;
            row.TokenHash = null;
        }
        else if (row.ProtectedKey is null)
        {
            var keyBytes = RandomNumberGenerator.GetBytes(32);
            var credential = Convert.ToBase64String(keyBytes);
            row.ProtectedKey = protector.Protect(credential);
            row.TokenHash = Convert.ToHexString(SHA256.HashData(keyBytes));
        }
        if (approved) row.Status = AppPermissionStatuses.Approved;
        row.UpdatedAtUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException) { throw new AppPermissionConflictException(); }
        if (audit is not null)
        {
            var (action, eventType) = row.Status switch
            {
                AppPermissionStatuses.Approved => (AuditActions.Approved, AuditEventTypes.AppPermissionApproved),
                AppPermissionStatuses.Revoked => (AuditActions.Revoked, AuditEventTypes.AppPermissionRevoked),
                _ => (AuditActions.Rejected, AuditEventTypes.AppPermissionRejected)
            };
            await audit.WriteAsync(new AuditEventWriteRequest
            {
                Action = action, EntityType = AuditEntityTypes.AppPermission, EntityId = appId, EventType = eventType,
                Message = $"App permission {permission} was {row.Status.ToLowerInvariant()}",
                Metadata = new { permission, appId }
            }, token);
        }
        return await GetStatusAsync(appId, token);
    }

    public async Task<(Guid AppId, string Permission)?> ValidateCredentialAsync(string tokenValue, CancellationToken token)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(tokenValue); }
        catch (FormatException) { return null; }
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var row = await db.AppPermissionApprovals.SingleOrDefaultAsync(x => x.TokenHash == hash, token);
        if (row is null) return null;
        var app = await db.WebApps.Where(item => item.Id == row.AppId)
            .Select(item => new { item.AccessStatus, item.ApplicationContractJson }).SingleOrDefaultAsync(token);
        if (app is null || app.AccessStatus != AppAccessStatuses.Active
            || !RequestedPermissions(app.ApplicationContractJson).Contains(row.Permission, StringComparer.Ordinal)) return null;
        return (row.AppId, row.Permission);
    }

    private async Task<string[]> RequestedAsync(Guid appId, CancellationToken token)
    {
        var json = await db.WebApps.Where(app => app.Id == appId).Select(app => app.ApplicationContractJson).SingleOrDefaultAsync(token);
        if (json is null) throw new KeyNotFoundException("App not found.");
        return RequestedPermissions(json);
    }

    // A stored contract that no longer reads requests nothing; the next deployment stores a new one.
    private static string[] RequestedPermissions(string json)
    {
        try { return ApplicationContractSerialization.Deserialize(json).Permissions.ToArray(); }
        catch (InvalidOperationException) { return []; }
        catch (System.Text.Json.JsonException) { return []; }
    }
}

/// <summary>Another administrator changed the same permission at the same time.</summary>
public sealed class AppPermissionConflictException()
    : Exception("The permission was changed at the same time. Reload and try again.");
