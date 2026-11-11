using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>What changes if the owner updates an installed catalog app to the commit its catalog pins today.</summary>
public sealed record AppCatalogUpdateModel(
    bool Available,
    string? InstalledCommit,
    string? LatestCommit,
    string? CompareUrl,
    bool Edited,
    IReadOnlyList<string> NewSecrets,
    IReadOnlyList<string> NewSettings,
    IReadOnlyList<string> NewPermissions);

public sealed class AppCatalogUpdateRequest
{
    /// <summary>Required when the app's files were edited after the install: the update replaces those changes.</summary>
    public bool ConfirmEdited { get; set; }
}

/// <summary>
/// Updates an app installed from a catalog to the commit that catalog pins now. The owner asks for it; nothing runs by itself.
/// The files are replaced and the app is redeployed the way "Deploy" does, so the data folder, setting values, secrets,
/// sharing and schedules stay as they are, and a permission the new version adds waits for administrator approval.
/// </summary>
public sealed class AppCatalogUpdateService(
    ApplicationDbContext dbContext,
    AppsService appsService,
    UserContextService userContextService,
    ResourceAuthorizationService authorizationService,
    AppFileStorageService fileStorageService,
    AppCatalogService catalog,
    AppCatalogInstallService installer,
    StoredSourceDeploymentService deployment,
    Services.Interfaces.IAuditEventService auditEventService)
{
    /// <exception cref="KeyNotFoundException">The app does not exist for this user.</exception>
    public async Task<AppCatalogUpdateModel> GetAsync(Guid appId, CancellationToken cancellationToken)
    {
        var (app, ownerId, source) = await FindAsync(appId, cancellationToken);
        if (source is null) return None(app.InstalledFromCommit);
        var latest = await catalog.PinnedCommitAsync(source.Repo, source.Folder, cancellationToken);
        if (latest is null || latest == source.Commit) return None(source.Commit);

        using var installed = await installer.DownloadAsync(source, cancellationToken);
        using var newer = await installer.DownloadAsync(source with { Commit = latest }, cancellationToken);
        var current = ApplicationContractSerialization.Deserialize(app.ApplicationContractJson);
        var next = AppCatalogInstallService.ReadManifest(newer, source).Application;
        return new AppCatalogUpdateModel(true, source.Commit, latest,
            $"https://github.com/{source.Repo}/compare/{source.Commit}...{latest}",
            IsEdited(installed, source, fileStorageService.GetAppSourceDirectoryPath(ownerId, appId)),
            Added(current.Secrets.Select(x => x.Name), next.Secrets.Select(x => x.Name)),
            Added(current.Settings.Select(x => x.Name), next.Settings.Select(x => x.Name)),
            Added(current.Permissions, next.Permissions));
    }

    /// <exception cref="KeyNotFoundException">The app does not exist for this user.</exception>
    /// <exception cref="UnauthorizedAccessException">The user cannot update the app.</exception>
    /// <exception cref="InvalidOperationException">There is no update, or the edited files were not confirmed.</exception>
    public async Task<DeploymentOperationResponse> UpdateAsync(Guid appId, AppCatalogUpdateRequest request, CancellationToken cancellationToken)
    {
        var userId = userContextService.GetUserId();
        var (app, ownerId, source) = await FindAsync(appId, cancellationToken);
        if (!await authorizationService.CanUpdateAppAsync(appId, userId, cancellationToken))
            throw new UnauthorizedAccessException("You do not have permission to update this app.");
        var update = await GetAsync(appId, cancellationToken);
        if (!update.Available || source is null) throw new InvalidOperationException("This app has no update.");
        if (update.Edited && !request.ConfirmEdited)
            throw new InvalidOperationException("This app was edited after it was installed. Confirm that the update replaces your changes.");

        var target = source with { Commit = update.LatestCommit! };
        using (var package = await installer.DownloadAsync(target, cancellationToken))
        {
            var directory = fileStorageService.GetAppSourceDirectoryPath(ownerId, appId);
            if (Directory.Exists(directory))
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                    if (entry is DirectoryInfo folder) folder.Delete(true); else entry.Delete();
            await AppCatalogInstallService.ExtractAsync(package, target, directory, cancellationToken);
        }

        var entity = await dbContext.WebApps.FirstAsync(x => x.Id == appId, cancellationToken);
        entity.InstalledFromCommit = target.Commit;
        entity.Updated = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.InstalledFromSource,
            EntityType = AuditEntityTypes.App,
            EntityId = appId,
            EventType = AuditEventTypes.AppInstalledFromSource,
            Message = $"App '{entity.Name}' was updated from {source.Repo} to {target.Commit[..7]}",
            Outcome = AuditOutcomes.Success,
            ActorUserId = userId,
            Metadata = new { source.Repo, source.Folder, From = source.Commit, To = target.Commit }
        });
        return await deployment.AcceptAsync(appId, userId, ownerId, null, false, cancellationToken);
    }

    private async Task<(WebApp App, Guid OwnerId, GitAppSource? Source)> FindAsync(Guid appId, CancellationToken cancellationToken)
    {
        var userId = userContextService.GetUserId();
        if (!await authorizationService.CanAccessAppAsync(appId, userId, cancellationToken))
            throw new KeyNotFoundException("App not found.");
        var info = await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken) ?? throw new KeyNotFoundException("App not found.");
        var ownerId = info.OwnerId ?? userId;
        var app = await dbContext.WebApps.AsNoTracking().FirstAsync(x => x.Id == appId, cancellationToken);
        var valid = app.InstalledFromRepo is { Length: > 0 } && app.InstalledFromCommit is { Length: > 0 }
            && AppCatalogInstallService.IsValidSource(app.InstalledFromRepo, (app.InstalledFromFolder ?? "").Trim('/'), app.InstalledFromCommit);
        return (app, ownerId, valid ? new GitAppSource(app.InstalledFromRepo!, (app.InstalledFromFolder ?? "").Trim('/'), app.InstalledFromCommit!) : null);
    }

    private static AppCatalogUpdateModel None(string? commit) => new(false, commit, null, null, false, [], [], []);

    internal static IReadOnlyList<string> Added(IEnumerable<string> before, IEnumerable<string> after) =>
        after.Except(before, StringComparer.Ordinal).ToList();

    /// <summary>True when the files on the server differ from the installed commit (changed, added or removed).</summary>
    internal static bool IsEdited(System.IO.Compression.ZipArchive installed, GitAppSource source, string directory)
    {
        if (!Directory.Exists(directory)) return true;
        var root = Path.GetFullPath(directory);
        var onDisk = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.StartsWith(".git/", StringComparison.Ordinal) && !path.StartsWith("node_modules/", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (entry, relative) in AppCatalogInstallService.AppFiles(installed, source))
        {
            if (!onDisk.Remove(relative)) return true;
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            if (!memory.GetBuffer().AsSpan(0, (int)memory.Length).SequenceEqual(File.ReadAllBytes(Path.Combine(root, relative)))) return true;
        }
        return onDisk.Count > 0;
    }
}
