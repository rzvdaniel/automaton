using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileSystemGlobbing;

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
    IReadOnlyList<string> NewPermissions,
    bool DeployFailed = false,
    string? DeployError = null,
    string? RunningCommit = null,
    bool HasPreviousVersion = false);

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
    AppCatalogBackfillService backfill,
    StoredSourceDeploymentService deployment,
    Services.Interfaces.IAuditEventService auditEventService,
    IMemoryCache cache)
{
    // The Overview asks on every visit and the answer needs two downloads, so it is kept for a few minutes.
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    /// <exception cref="KeyNotFoundException">The app does not exist for this user.</exception>
    public Task<AppCatalogUpdateModel> GetAsync(Guid appId, CancellationToken cancellationToken, bool refresh = false) =>
        GetAsync(appId, fresh: false, cancellationToken, refresh);

    /// <summary>With refresh, the catalog is read again first (at most once a minute per source) so a pin changed minutes ago is seen.</summary>
    private async Task<AppCatalogUpdateModel> GetAsync(Guid appId, bool fresh, CancellationToken cancellationToken, bool refresh = false)
    {
        var (app, ownerId, source) = await FindAsync(appId, cancellationToken);
        if (source is null) return None(app.InstalledFromCommit);
        var (failure, hasDeployed) = await DeployStateAsync(dbContext, appId, cancellationToken);
        // Apps deployed before DeployedFromCommit existed have no recorded commit: say "previous version", never guess one.
        var running = app.DeployedFromCommit;
        var latest = await catalog.PinnedCommitAsync(source.Repo, source.Folder, cancellationToken, refresh);
        // The last deploy failed: the app still runs the previous version, so it is not up to date, and an update retries the deploy.
        if (latest is null || latest == source.Commit)
            return failure is null ? None(source.Commit) with { RunningCommit = running, HasPreviousVersion = hasDeployed }
                : new AppCatalogUpdateModel(true, source.Commit, source.Commit, null, false, [], [], [], true, failure, running, hasDeployed);
        // The edited flag changes when the app's files do, so the key includes their fingerprint (#1149).
        var key = $"catalog-update:{appId}:{source.Commit}:{latest}:{SourceFingerprint(fileStorageService.GetAppSourceDirectoryPath(ownerId, appId))}";
        if (!fresh && cache.TryGetValue(key, out AppCatalogUpdateModel? cached) && cached is not null) return WithDeploy(cached, failure, running, hasDeployed);
        var model = await CompareAsync(app, ownerId, source, latest, cancellationToken);
        cache.Set(key, model, CacheFor);
        return WithDeploy(model, failure, running, hasDeployed);
    }

    internal static AppCatalogUpdateModel WithDeploy(AppCatalogUpdateModel model, string? failure, string? running, bool hasDeployed) =>
        model with { DeployFailed = failure is not null, DeployError = failure, RunningCommit = running, HasPreviousVersion = hasDeployed };

    /// <summary>The reason of the app's last deploy when the live target is Failed and that deploy failed (else null), and whether a deploy ever succeeded.</summary>
    internal static async Task<(string? Error, bool HasDeployed)> DeployStateAsync(ApplicationDbContext dbContext, Guid appId, CancellationToken cancellationToken)
    {
        var target = await dbContext.AppDeploymentTargets.AsNoTracking()
            .Where(t => t.WebAppId == appId && t.IsDefault)
            .Select(t => new { t.Status, t.LastSuccessfulDeploymentAt })
            .FirstOrDefaultAsync(cancellationToken);
        var hasDeployed = target?.LastSuccessfulDeploymentAt is not null;
        if (target?.Status != DeploymentTargetStatus.Failed) return (null, hasDeployed);
        var latest = await dbContext.DeploymentOperations.AsNoTracking()
            .Where(o => o.WebAppId == appId && (o.TargetSlug == null ||
                dbContext.AppDeploymentTargets.Any(t => t.WebAppId == appId && t.IsDefault && t.Slug == o.TargetSlug)))
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new { o.Status, o.ErrorMessage })
            .FirstOrDefaultAsync(cancellationToken);
        return (latest?.Status == DeploymentOperationStatuses.Failed ? latest.ErrorMessage ?? "The deploy failed." : null, hasDeployed);
    }

    /// <summary>Changes whenever a file of the app is added, removed, resized or rewritten.</summary>
    internal static string SourceFingerprint(string directory)
    {
        if (!Directory.Exists(directory)) return "none";
        long count = 0, length = 0, newest = 0;
        try
        {
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                count++;
                length += file.Length;
                newest = Math.Max(newest, file.LastWriteTimeUtc.Ticks);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "unreadable-" + Guid.NewGuid().ToString("N"); // files changed during the scan: never reuse a cached answer
        }
        return $"{count}-{length}-{newest}";
    }

    private async Task<AppCatalogUpdateModel> CompareAsync(WebApp app, Guid ownerId, GitAppSource source, string latest, CancellationToken cancellationToken)
    {
        var appId = app.Id;

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
        var update = await GetAsync(appId, fresh: true, cancellationToken);
        if (!update.Available || source is null) throw new InvalidOperationException("This app has no update.");
        if (update.Edited && !request.ConfirmEdited)
            throw new InvalidOperationException("This app was edited after it was installed. Confirm that the update replaces your changes.");

        // Same commit as installed: the files are in place and only the deploy failed, so retry it.
        if (update.LatestCommit == source.Commit)
            return await deployment.AcceptAsync(appId, userId, ownerId, null, false, cancellationToken);

        var target = source with { Commit = update.LatestCommit! };
        using (var package = await installer.DownloadAsync(target, cancellationToken))
        {
            // Extract next to the app first and swap only when that worked, so a failed download or extract leaves the
            // installed files as they were.
            var directory = Path.GetFullPath(fileStorageService.GetAppSourceDirectoryPath(ownerId, appId)).TrimEnd(Path.DirectorySeparatorChar);
            var staging = $"{directory}.update-{Guid.NewGuid():N}";
            var previous = $"{directory}.previous-{Guid.NewGuid():N}";
            try
            {
                Directory.CreateDirectory(staging);
                await AppCatalogInstallService.ExtractAsync(package, target, staging, cancellationToken);
                KeepServerWrittenFiles(directory, staging);
                if (Directory.Exists(directory)) Directory.Move(directory, previous);
                Directory.Move(staging, directory);
            }
            catch
            {
                if (!Directory.Exists(directory) && Directory.Exists(previous)) Directory.Move(previous, directory);
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                throw;
            }
            if (Directory.Exists(previous)) Directory.Delete(previous, true);
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
        if (await authorizationService.CanUpdateAppAsync(appId, userId, cancellationToken) && await backfill.TryLinkAsync(appId, ownerId, cancellationToken))
            app = await dbContext.WebApps.AsNoTracking().FirstAsync(x => x.Id == appId, cancellationToken);
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
        var files = AppCatalogInstallService.AppFiles(installed, source).ToList();
        var notStored = NotStoredMatcher(files);
        foreach (var (entry, relative) in files)
        {
            if (!onDisk.Remove(relative))
            {
                // Files the deploy rules leave out (AGENTS.md, the app's .automatonignore entries) are not kept on the server.
                if (notStored.Match(relative).HasMatches) continue;
                return true;
            }
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var original = memory.GetBuffer().AsSpan(0, (int)memory.Length).ToArray();
            var current = File.ReadAllBytes(Path.Combine(root, relative));
            if (original.AsSpan().SequenceEqual(current)) continue;
            if (relative == ManifestFile && SameManifest(original, current)) continue;
            if (relative == IgnoreFile) continue;
            return true;
        }
        onDisk.Remove(IgnoreFile);
        return onDisk.Count > 0;
    }

    private static Microsoft.Extensions.FileSystemGlobbing.Matcher NotStoredMatcher(IEnumerable<(System.IO.Compression.ZipArchiveEntry Entry, string Path)> files)
    {
        var matcher = new Microsoft.Extensions.FileSystemGlobbing.Matcher(StringComparison.Ordinal);
        matcher.AddInclude("AGENTS.md");
        matcher.AddInclude("CLAUDE.md");
        var ignore = files.FirstOrDefault(file => file.Path == IgnoreFile).Entry;
        if (ignore is null) return matcher;
        using var reader = new StreamReader(ignore.Open());
        foreach (var raw in reader.ReadToEnd().Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or '!') continue;
            line = line.TrimStart('/');
            matcher.AddInclude(line.EndsWith('/') ? line + "**" : line);
            if (!line.Contains('/')) matcher.AddInclude("**/" + (line.EndsWith('/') ? line + "**" : line));
        }
        return matcher;
    }

    private const string ManifestFile = "automaton.json";
    private const string IgnoreFile = ".automatonignore";

    /// <summary>Fields the server and the CLI write into automaton.json themselves (publishing, naming); they are not code edits.</summary>
    internal static readonly string[] ServerWrittenFields = ["appId", "name", "slug", "server"];

    private static System.Text.Json.Nodes.JsonObject? ParseManifest(byte[] bytes)
    {
        try { return System.Text.Json.Nodes.JsonNode.Parse(bytes) as System.Text.Json.Nodes.JsonObject; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>True when two manifests differ only in the fields the server writes.</summary>
    internal static bool SameManifest(byte[] original, byte[] current)
    {
        var a = ParseManifest(original);
        var b = ParseManifest(current);
        if (a is null || b is null) return false;
        foreach (var field in ServerWrittenFields) { a.Remove(field); b.Remove(field); }
        DropDefaults(a);
        DropDefaults(b);
        return System.Text.Json.Nodes.JsonNode.DeepEquals(a, b);
    }

    /// <summary>Removes false, null and empty-object values: the server writes these defaults when it re-serialises the manifest, so a missing one equals a default one.</summary>
    private static void DropDefaults(System.Text.Json.Nodes.JsonObject node)
    {
        foreach (var (key, value) in node.ToList())
        {
            if (value is System.Text.Json.Nodes.JsonObject child) DropDefaults(child);
            if (value is null
                || value is System.Text.Json.Nodes.JsonObject { Count: 0 }
                || value is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<bool>(out var flag) && !flag)
                node.Remove(key);
        }
    }

    /// <summary>Carries the name, slug and other publish fields of the installed manifest, and .automatonignore, into the freshly extracted files.</summary>
    internal static void KeepServerWrittenFiles(string installedDirectory, string stagingDirectory)
    {
        var oldIgnore = Path.Combine(installedDirectory, IgnoreFile);
        var newIgnore = Path.Combine(stagingDirectory, IgnoreFile);
        if (File.Exists(oldIgnore) && !File.Exists(newIgnore)) File.Copy(oldIgnore, newIgnore);

        var oldPath = Path.Combine(installedDirectory, ManifestFile);
        var newPath = Path.Combine(stagingDirectory, ManifestFile);
        if (!File.Exists(oldPath) || !File.Exists(newPath)) return;
        var old = ParseManifest(File.ReadAllBytes(oldPath));
        var next = ParseManifest(File.ReadAllBytes(newPath));
        if (old is null || next is null) return;
        var changed = false;
        foreach (var field in ServerWrittenFields)
        {
            if (!old.TryGetPropertyValue(field, out var value) || value is null) continue;
            next[field] = value.DeepClone();
            changed = true;
        }
        if (changed)
            File.WriteAllText(newPath, next.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
