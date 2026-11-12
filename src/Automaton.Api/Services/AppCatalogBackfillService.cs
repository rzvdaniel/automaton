using Automaton.Api.Data;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Automaton.Api.Services;

/// <summary>
/// Links an app installed from a catalog before install sources were recorded (#718) to its catalog entry, so Update is offered.
/// Done lazily when the Overview asks for the update; it only touches an app that has no recorded source, is not a fork, and
/// is named like exactly one public catalog entry. The recorded commit is the pinned commit whose files match the app's files,
/// or the oldest pinned commit in the catalog's history when none match (an edited app then shows "edited" on Update).
/// </summary>
public sealed class AppCatalogBackfillService(
    ApplicationDbContext dbContext,
    AppCatalogService catalog,
    AppCatalogInstallService installer,
    AppFileStorageService fileStorageService,
    IMemoryCache cache,
    ILogger<AppCatalogBackfillService> logger)
{
    private const int HistoryLimit = 30;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    /// <summary>True when the app was linked to a catalog entry now.</summary>
    public async Task<bool> TryLinkAsync(Guid appId, Guid ownerId, CancellationToken cancellationToken)
    {
        var key = $"catalog-backfill:{appId}";
        if (cache.TryGetValue(key, out _)) return false;
        var app = await dbContext.WebApps.AsNoTracking().Where(x => x.Id == appId)
            .Select(x => new { x.Name, x.InstalledFromRepo, x.InstalledFromFolder, x.InstalledFromCommit, x.ForkedFromAppId }).FirstOrDefaultAsync(cancellationToken);
        if (app is null || !IsCandidate(app.InstalledFromRepo, app.InstalledFromFolder, app.InstalledFromCommit, app.ForkedFromAppId)) return false;
        cache.Set(key, true, RetryAfter);

        var entries = await catalog.PublicEntriesAsync(cancellationToken);
        var named = entries.Where(e => string.Equals(e.App.Name, app.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count != 1) return false;
        var (source, repo, entry) = named[0];

        var history = await catalog.PinnedHistoryAsync(source, repo, entry.Folder, HistoryLimit, cancellationToken);
        if (!history.Contains(entry.Commit)) history = [entry.Commit, .. history];
        var directory = fileStorageService.GetAppSourceDirectoryPath(ownerId, appId);
        var commit = history[^1];
        foreach (var candidate in history)
        {
            try
            {
                var git = new GitAppSource(repo, entry.Folder.Trim('/'), candidate);
                using var package = await installer.DownloadAsync(git, cancellationToken);
                if (AppCatalogUpdateService.IsEdited(package, git, directory)) continue;
                commit = candidate;
                break;
            }
            catch (ArgumentException exception)
            {
                logger.LogWarning("Catalog commit {Commit} could not be compared: {Reason}", candidate, exception.Message);
            }
        }

        var entity = await dbContext.WebApps.FirstAsync(x => x.Id == appId, cancellationToken);
        // Check again on the tracked row: another request may have linked it meanwhile.
        if (!IsCandidate(entity.InstalledFromRepo, entity.InstalledFromFolder, entity.InstalledFromCommit, entity.ForkedFromAppId)) return false;
        entity.InstalledFromRepo = repo;
        entity.InstalledFromFolder = entry.Folder.Trim('/');
        entity.InstalledFromCommit = commit;
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Linked app {AppId} to catalog entry {Repo}/{Folder} at {Commit}.", appId, repo, entry.Folder, commit);
        return true;
    }

    /// <summary>No recorded source at all and not a fork: only then may the server decide where an app came from.</summary>
    internal static bool IsCandidate(string? repo, string? folder, string? commit, Guid? forkedFrom) =>
        string.IsNullOrEmpty(repo) && string.IsNullOrEmpty(folder) && string.IsNullOrEmpty(commit) && forkedFrom is null;
}
