using System.Security.Cryptography;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed record AppPermissionStatus(string Permission, bool Requested, bool Approved, string Status);

/// <summary>System administrator approval and scoped credentials for app capabilities.</summary>
public sealed class AppPermissionService(
    ApplicationDbContext db,
    ISecretProtector protector)
{
    public async Task<IReadOnlyList<AppPermissionStatus>> GetStatusAsync(Guid appId, CancellationToken token)
    {
        var requested = await RequestedAsync(appId, token);
        var approvals = await db.AppPermissionApprovals.Where(x => x.AppId == appId)
            .ToDictionaryAsync(x => x.Permission, token);
        return requested.Select(permission =>
        {
            var found = approvals.TryGetValue(permission, out var approval);
            var status = found ? approval!.Status : "Pending";
            return new AppPermissionStatus(permission, true, found && approval!.ProtectedKey is not null, status);
        }).ToArray();
    }

    public async Task<IReadOnlyList<AppPermissionStatus>> SetApprovalAsync(Guid appId, string permission, bool approved,
        CancellationToken token)
    {
        if (permission != "backups:read") throw new ArgumentException("Unsupported app permission.", nameof(permission));
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
            row.Status = row.ProtectedKey is null ? "Rejected" : "Revoked";
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
        if (approved) row.Status = "Approved";
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        return await GetStatusAsync(appId, token);
    }

    public async Task<(Guid AppId, string Permission)?> ValidateCredentialAsync(string tokenValue, CancellationToken token)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(tokenValue); }
        catch (FormatException) { return null; }
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var row = await db.AppPermissionApprovals.SingleOrDefaultAsync(x => x.TokenHash == hash, token);
        if (row is null || !(await RequestedAsync(row.AppId, token)).Contains(row.Permission, StringComparer.Ordinal)) return null;
        return (row.AppId, row.Permission);
    }

    private async Task<string[]> RequestedAsync(Guid appId, CancellationToken token)
    {
        var json = await db.WebApps.Where(app => app.Id == appId).Select(app => app.ApplicationContractJson).SingleOrDefaultAsync(token);
        if (json is null) throw new KeyNotFoundException("App not found.");
        return ApplicationContractSerialization.Deserialize(json).Permissions.ToArray();
    }
}
