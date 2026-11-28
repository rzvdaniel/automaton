using System.Text;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed record AppDataGrantDto(
    Guid Id, Guid SourceAppId, string SourceAppName, Guid GranteeAppId, string GranteeAppName,
    string Volume, bool ReadWrite, DateTimeOffset CreatedAt);

public sealed record AppDataGrantsState(
    IReadOnlyList<string> Volumes, IReadOnlyList<AppDataGrantDto> GrantedOut, IReadOnlyList<AppDataGrantDto> GrantedIn);

/// <summary>
/// Owner-approved grants that let one app (an admin tool) mount a data volume of another app. A grant is read-only unless
/// read-write was confirmed a second time; every change is logged and restarts the receiving app so it takes effect.
/// </summary>
public sealed class AppDataGrantService(
    ApplicationDbContext db,
    ResourceAuthorizationService authorization,
    UserContextService user,
    RecentActivityService activity,
    IAuditEventService audit,
    IAppRuntimeRestarter restarter)
{
    public const string MountRoot = "/granted";

    public async Task<AppDataGrantsState> GetAsync(Guid appId, CancellationToken token)
    {
        var app = await LoadManagedAsync(appId, token);
        var rows = await db.AppDataGrants.Where(g => g.SourceAppId == appId || g.GranteeAppId == appId)
            .OrderBy(g => g.CreatedAt).ToListAsync(token);
        var names = await NamesAsync(rows, token);
        AppDataGrantDto Map(AppDataGrant g) => new(g.Id, g.SourceAppId, names[g.SourceAppId], g.GranteeAppId,
            names[g.GranteeAppId], g.Volume, g.ReadWrite, g.CreatedAt);
        return new(DeclaredVolumes(app),
            rows.Where(g => g.SourceAppId == appId).Select(Map).ToList(),
            rows.Where(g => g.GranteeAppId == appId).Select(Map).ToList());
    }

    public async Task<AppDataGrantsState> AddAsync(
        Guid sourceAppId, Guid granteeAppId, string? volume, bool readWrite, bool confirmReadWrite, CancellationToken token)
    {
        var source = await LoadManagedAsync(sourceAppId, token);
        var grantee = await LoadManagedAsync(granteeAppId, token);
        if (sourceAppId == granteeAppId) throw new InvalidOperationException("An app already has access to its own data.");
        if (volume is null || !DeclaredVolumes(source).Contains(volume, StringComparer.Ordinal))
            throw new InvalidOperationException("The app has no data volume with this name.");
        if (readWrite && !confirmReadWrite)
            throw new InvalidOperationException("Read-write access needs a second confirmation.");
        if (!HasCompose(grantee.ApplicationContractJson))
            throw new InvalidOperationException("Only an app that runs as a container can be given access to another app's data.");

        var existing = await db.AppDataGrants.SingleOrDefaultAsync(
            g => g.SourceAppId == sourceAppId && g.GranteeAppId == granteeAppId && g.Volume == volume, token);
        if (existing is null)
            db.AppDataGrants.Add(new AppDataGrant
            {
                Id = Guid.NewGuid(), SourceAppId = sourceAppId, GranteeAppId = granteeAppId, Volume = volume,
                ReadWrite = readWrite, CreatedByUserId = user.GetUserId()
            });
        else if (existing.ReadWrite == readWrite)
            return await GetAsync(sourceAppId, token);
        else
            existing.ReadWrite = readWrite;
        await db.SaveChangesAsync(token);
        await RecordAsync(ActivityType.AppDataGrantAdded, AuditEventTypes.AppDataGrantAdded, AuditActions.Added,
            source, grantee, volume, readWrite, token);
        await restarter.RestartWithComposeAsync(granteeAppId, CancellationToken.None);
        return await GetAsync(sourceAppId, token);
    }

    public async Task<AppDataGrantsState> RemoveAsync(Guid appId, Guid grantId, CancellationToken token)
    {
        await LoadManagedAsync(appId, token);
        var grant = await db.AppDataGrants.SingleOrDefaultAsync(
            g => g.Id == grantId && (g.SourceAppId == appId || g.GranteeAppId == appId), token)
            ?? throw new KeyNotFoundException("Grant not found");
        var source = await LoadAsync(grant.SourceAppId, token);
        var grantee = await LoadAsync(grant.GranteeAppId, token);
        db.AppDataGrants.Remove(grant);
        await db.SaveChangesAsync(token);
        await RecordAsync(ActivityType.AppDataGrantRemoved, AuditEventTypes.AppDataGrantRemoved, AuditActions.Removed,
            source, grantee, grant.Volume, grant.ReadWrite, token);
        await restarter.RestartWithComposeAsync(grant.GranteeAppId, CancellationToken.None);
        return await GetAsync(appId, token);
    }

    /// <summary>The mounts the receiving app gets when it starts on this Runner. Only grants whose volume still exists and lives on the same Runner count.</summary>
    public async Task<IReadOnlyList<HostContainerGrantedMount>> ResolveMountsAsync(
        Guid granteeAppId, Guid machineRunnerId, CancellationToken token)
    {
        var grants = await db.AppDataGrants.AsNoTracking().Where(g => g.GranteeAppId == granteeAppId)
            .OrderBy(g => g.CreatedAt).ToListAsync(token);
        var mounts = new List<HostContainerGrantedMount>();
        foreach (var grant in grants)
        {
            var source = await db.WebApps.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == grant.SourceAppId && a.DeletionClaimId == null, token);
            var target = await db.AppDeploymentTargets.AsNoTracking().SingleOrDefaultAsync(
                t => t.WebAppId == grant.SourceAppId && t.IsDefault && t.ExecutionRunnerId == machineRunnerId, token);
            if (source is null || target is null || !DeclaredVolumes(source).Contains(grant.Volume, StringComparer.Ordinal))
                continue;
            mounts.Add(new(source.Id, target.Id, grant.Volume,
                $"{MountRoot}/{SafeName(source)}/{grant.Volume}", !grant.ReadWrite));
        }
        return mounts;
    }

    internal static string SafeName(WebApp app)
    {
        var text = new StringBuilder();
        foreach (var c in app.Name.ToLowerInvariant())
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') text.Append(c);
            else if (text.Length > 0 && text[^1] != '-') text.Append('-');
        var slug = text.ToString().Trim('-');
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        return (slug.Length == 0 ? "app" : slug) + "-" + app.Id.ToString("N")[..6];
    }

    private static IReadOnlyList<string> DeclaredVolumes(WebApp app)
    {
        try
        {
            return ApplicationContractSerialization.Deserialize(app.ApplicationContractJson).Compose?.Volumes ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static bool HasCompose(string json)
    {
        try { return ApplicationContractSerialization.Deserialize(json).Compose is not null; }
        catch (Exception) { return false; }
    }

    private async Task<WebApp> LoadAsync(Guid appId, CancellationToken token)
        => await db.WebApps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appId && a.DeletionClaimId == null, token)
            ?? throw new KeyNotFoundException("App not found");

    private async Task<WebApp> LoadManagedAsync(Guid appId, CancellationToken token)
    {
        var app = await LoadAsync(appId, token);
        if (!await authorization.CanUpdateAppAsync(appId, token))
            throw new UnauthorizedAccessException("Only the people who manage an app can change who can reach its data.");
        return app;
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<AppDataGrant> rows, CancellationToken token)
    {
        var ids = rows.SelectMany(g => new[] { g.SourceAppId, g.GranteeAppId }).Distinct().ToList();
        return await db.WebApps.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, token);
    }

    private async Task RecordAsync(ActivityType type, string eventType, string action,
        WebApp source, WebApp grantee, string volume, bool readWrite, CancellationToken token)
    {
        await activity.LogAppActivityAsync(type, source.Name, source.Id, token);
        await audit.WriteAsync(new AuditEventWriteRequest
        {
            Action = action,
            EntityType = AuditEntityTypes.App,
            EntityId = source.Id,
            EventType = eventType,
            Message = $"{(action == AuditActions.Added ? "Gave" : "Removed")} '{grantee.Name}' {(readWrite ? "read-write" : "read-only")} access to volume '{volume}' of '{source.Name}'",
            Outcome = AuditOutcomes.Success,
            ActorUserId = user.GetUserId(),
            Metadata = new { SourceAppId = source.Id, GranteeAppId = grantee.Id, Volume = volume, ReadWrite = readWrite }
        });
    }
}
