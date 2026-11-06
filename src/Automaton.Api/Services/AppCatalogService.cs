using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Reads the app catalog (an index of installable apps) from the official automaton-apps repository and keeps the
/// last good copy, so a short GitHub outage does not empty the Catalog tab. Nothing is sent about this instance.
/// </summary>
public sealed class AppCatalogService(
    ApplicationDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<AppCatalogService> logger,
    CatalogSourceStore sources)
{
    public const string DefaultSourceUrl = "https://raw.githubusercontent.com/rzvdaniel/automaton-apps/main/catalog.json";
    internal const string CacheKey = "Apps.Catalog.Cache";
    internal const int SupportedSchemaVersion = 1;
    private const long MaximumBytes = 1024 * 1024;
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static string CacheKeyFor(string sourceId) => sourceId == CatalogSourceStore.OfficialId ? CacheKey : $"{CacheKey}.{sourceId}";

    public async Task<CatalogResponse> GetAsync(Guid userId, bool refresh, CancellationToken cancellationToken, CatalogQuery? query = null)
    {
        var enabled = await sources.EnabledAsync(cancellationToken);
        var installed = await InstalledAsync(userId, cancellationToken);
        var version = ParseVersion(configuration["Installation:Version"]);
        var apps = new List<CatalogEntryDto>();
        var problems = new List<string>();
        DateTime? fetchedAt = null;
        var stale = false;
        var loaded = 0;

        foreach (var source in enabled)
        {
            var (entry, failure) = await LoadSourceAsync(source, refresh, cancellationToken);
            var label = enabled.Count > 1 ? $"{source.Name}: " : "";
            if (entry is null)
            {
                problems.Add($"{label}{failure ?? "The catalog could not be loaded."}");
                continue;
            }
            loaded++;
            fetchedAt = fetchedAt is null || entry.FetchedAt < fetchedAt ? entry.FetchedAt : fetchedAt;
            if (failure is not null)
            {
                stale = true;
                problems.Add($"{label}{failure} Showing the list from {entry.FetchedAt:u}.");
            }
            apps.AddRange(entry.Document.Apps.Select(app => ToDto(app, entry.Document.Repo, installed, version, source)));
        }

        if (loaded == 0 && enabled.Count > 0)
            return new CatalogResponse([], null, false, true, string.Join(" ", problems));
        if (enabled.Count == 0)
            return new CatalogResponse([], null, false, false, "No catalog source is turned on.");
        var message = problems.Count == 0 ? null : string.Join(" ", problems);
        return Shape(apps, query ?? new CatalogQuery(), fetchedAt, stale, message);
    }

    /// <summary>Applies the filters and the page to the whole catalog and adds the filter options with their counts.</summary>
    internal static CatalogResponse Shape(List<CatalogEntryDto> all, CatalogQuery query, DateTime? fetchedAt, bool stale, string? message)
    {
        var text = query.Q?.Trim();
        var matches = all.Where(app =>
            (string.IsNullOrEmpty(text)
                || app.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || app.Summary.Contains(text, StringComparison.OrdinalIgnoreCase)
                || app.Tags?.Any(tag => tag.Contains(text, StringComparison.OrdinalIgnoreCase)) == true)
            && (string.IsNullOrEmpty(query.Category) || string.Equals(app.Category, query.Category, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(query.Source) || app.SourceId == query.Source)
            && (!query.Installed || app.InstalledAppId is not null)).ToList();

        var pageSize = query.PageSize <= 0 ? CatalogQuery.DefaultPageSize : Math.Min(query.PageSize, CatalogQuery.MaximumPageSize);
        var lastPage = Math.Max(1, (int)Math.Ceiling(matches.Count / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, lastPage);
        var categories = all.Where(app => app.Category.Length > 0).GroupBy(app => app.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => new CatalogFacet(g.Key, g.Key, g.Count())).ToList();
        var sources = all.GroupBy(app => (app.SourceId, app.SourceName))
            .OrderBy(g => g.Key.SourceName, StringComparer.OrdinalIgnoreCase).Select(g => new CatalogFacet(g.Key.SourceId, g.Key.SourceName, g.Count())).ToList();

        return new CatalogResponse(matches.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            fetchedAt, stale, false, message, matches.Count, all.Count, page, pageSize, categories, sources);
    }

    private async Task<(CacheEntry? Entry, string? Failure)> LoadSourceAsync(CatalogSourceStore.SourceRef source, bool refresh, CancellationToken cancellationToken)
    {
        var cached = await LoadCacheAsync(source, cancellationToken);
        string? failure = null;
        // A forced refresh still waits out a short pause, so a signed-in user cannot make the server hammer GitHub.
        var age = cached is null ? TimeSpan.MaxValue : DateTime.UtcNow - cached.FetchedAt;
        if (cached is null || age > FreshFor || (refresh && age > TimeSpan.FromSeconds(configuration.GetValue("Catalog:MinimumRefreshSeconds", 30))))
        {
            var (updated, error) = await FetchAsync(source, cached, cancellationToken);
            failure = error;
            if (updated is not null) await SaveCacheAsync(source, updated, cancellationToken);
            cached = updated ?? cached;
        }
        return (cached, failure);
    }

    /// <summary>Reads a not yet saved source once, so an administrator hears at once that it cannot be used. With save false (a dry run) nothing is stored.</summary>
    public async Task<(string? Problem, int Apps)> ProbeAsync(CatalogSourceStore.SourceRef source, CancellationToken cancellationToken, bool save = true)
    {
        var (entry, error) = await FetchAsync(source, null, cancellationToken);
        if (entry is null) return (error ?? "The catalog could not be loaded.", 0);
        if (save) await SaveCacheAsync(source, entry, cancellationToken);
        return (null, entry.Document.Apps.Count);
    }

    /// <summary>The read token of a private source, only for an app that source's catalog lists at exactly that folder and commit.</summary>
    public async Task<string?> TokenForAsync(string repo, string folder, string commit, CancellationToken cancellationToken)
    {
        foreach (var source in (await sources.EnabledAsync(cancellationToken)).Where(s => s.Token is not null
                     && string.Equals(s.Repo, repo, StringComparison.OrdinalIgnoreCase)))
        {
            var cached = await LoadCacheAsync(source, cancellationToken);
            if (cached?.Document.Apps.Any(app => app.Folder == folder && app.Commit == commit) == true) return source.Token;
        }
        return null;
    }

    private HttpRequestMessage RequestFor(CatalogSourceStore.SourceRef source, string? etag)
    {
        HttpRequestMessage request;
        if (source.Official)
        {
            var url = configuration["Catalog:SourceUrl"] is { Length: > 0 } configured ? configured : DefaultSourceUrl;
            request = new HttpRequestMessage(HttpMethod.Get, url);
        }
        else if (source.Token is not null)
        {
            var api = (configuration["Catalog:ApiBaseUrl"] is { Length: > 0 } a ? a : "https://api.github.com").TrimEnd('/');
            request = new HttpRequestMessage(HttpMethod.Get, $"{api}/repos/{source.Repo}/contents/catalog.json?ref={Uri.EscapeDataString(source.Branch)}");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github.raw+json");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {source.Token}");
        }
        else
        {
            var raw = (configuration["Catalog:RawBaseUrl"] is { Length: > 0 } r ? r : "https://raw.githubusercontent.com").TrimEnd('/');
            request = new HttpRequestMessage(HttpMethod.Get, $"{raw}/{source.Repo}/{source.Branch}/catalog.json");
        }
        request.Headers.UserAgent.ParseAdd("Automaton-Server");
        if (etag is { Length: > 0 }) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        return request;
    }

    private async Task<(CacheEntry? Updated, string? Error)> FetchAsync(CatalogSourceStore.SourceRef source, CacheEntry? cached, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var request = RequestFor(source, cached?.ETag);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cached is not null)
                return (cached with { FetchedAt = DateTime.UtcNow }, null);

            if (!response.IsSuccessStatusCode)
                return (null, source.Official
                    ? $"The catalog source answered {(int)response.StatusCode}."
                    : $"GitHub answered {(int)response.StatusCode} for {source.Repo} (branch {source.Branch}). Check the repository, the branch, that it has a catalog.json, and the token for a private repository.");

            var body = await ReadBoundedAsync(response, cancellationToken);
            var document = Parse(body, source.Official ? null : source.Repo);
            return (new CacheEntry(DateTime.UtcNow, response.Headers.ETag?.ToString(), body, document), null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            logger.LogWarning("Catalog refresh failed: {Reason}", exception.Message);
            return (null, exception is InvalidDataException or JsonException
                ? "The catalog could not be read."
                : "The catalog source could not be reached.");
        }
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaximumBytes) throw new InvalidDataException("The catalog is too large.");
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <param name="forcedRepo">For a custom source: entries always point at the source's own repository, whatever the file says,
    /// so a catalog cannot send an installer (or a private token) to another repository.</param>
    internal static CatalogDocument Parse(string body, string? forcedRepo = null)
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(body, Json)
            ?? throw new InvalidDataException("Empty catalog.");
        if (document.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException("Unsupported catalog schema version.");
        if (forcedRepo is not null) document.Repo = forcedRepo;
        if (string.IsNullOrWhiteSpace(document.Repo))
            throw new InvalidDataException("The catalog has no repository.");
        // One bad entry must not hide the others.
        document.Apps.RemoveAll(app => !AppCatalogInstallService.IsValidSource(document.Repo, app.Folder, app.Commit));
        return document;
    }

    private async Task<CacheEntry?> LoadCacheAsync(CatalogSourceStore.SourceRef source, CancellationToken cancellationToken)
    {
        var key = CacheKeyFor(source.Id);
        var setting = await dbContext.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting is null) return null;
        try
        {
            var stored = JsonSerializer.Deserialize<StoredCache>(setting.Value, Json);
            return stored is null ? null : new CacheEntry(stored.FetchedAt, stored.ETag, stored.Body, Parse(stored.Body, source.Official ? null : source.Repo));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private async Task SaveCacheAsync(CatalogSourceStore.SourceRef source, CacheEntry entry, CancellationToken cancellationToken)
    {
        var key = CacheKeyFor(source.Id);
        var value = JsonSerializer.Serialize(new StoredCache(entry.FetchedAt, entry.ETag, entry.Body), Json);
        var setting = await dbContext.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting is null)
            dbContext.Settings.Add(new Setting { Key = key, Value = value, Description = "Last good copy of the app catalog.", UpdatedAt = DateTime.UtcNow });
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Usually another request stored the first copy at the same moment; that copy is just as good.
            logger.LogWarning("The catalog copy could not be saved: {Reason}", exception.Message);
            foreach (var tracked in dbContext.ChangeTracker.Entries<Setting>().Where(e => e.Entity.Key == key).ToList())
                tracked.State = EntityState.Detached;
        }
    }

    private async Task<Dictionary<(string Repo, string Folder), Guid>> InstalledAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.WebApps
            .Where(app => app.InstalledFromRepo != null && app.WebAppUsers.Any(u => u.UserId == userId))
            .OrderByDescending(app => app.Created)
            .Select(app => new { app.Id, Repo = app.InstalledFromRepo!, Folder = app.InstalledFromFolder ?? "" })
            .ToListAsync(cancellationToken);
        var result = new Dictionary<(string, string), Guid>();
        foreach (var row in rows) result.TryAdd((row.Repo, row.Folder), row.Id);
        return result;
    }

    private static CatalogEntryDto ToDto(CatalogApp app, string repo, Dictionary<(string Repo, string Folder), Guid> installed, Version? serverVersion, CatalogSourceStore.SourceRef source)
    {
        var minimum = ParseVersion(app.MinServerVersion);
        var needsNewer = serverVersion is not null && minimum is not null && serverVersion < minimum;
        Guid? installedId = installed.TryGetValue((repo, app.Folder), out var id) ? id : null;
        return new CatalogEntryDto(app.Id, app.Name ?? "", app.Summary ?? "", app.Category ?? "", repo, app.Folder, app.Commit,
            app.MinServerVersion, app.Permissions, needsNewer, installedId, source.Id, source.Name, !source.Official, app.Tags?.Where(tag => tag is not null).ToList(), app.Icon);
    }

    internal static Version? ParseVersion(string? text)
    {
        var plain = text?.Trim();
        if (plain is not null && plain.StartsWith("server-v", StringComparison.Ordinal)) plain = plain["server-v".Length..];
        return Version.TryParse(plain, out var version) ? version : null;
    }

    internal sealed record CacheEntry(DateTime FetchedAt, string? ETag, string Body, CatalogDocument Document);
    private sealed record StoredCache(DateTime FetchedAt, string? ETag, string Body);

    internal sealed class CatalogDocument
    {
        public int SchemaVersion { get; set; }
        public string Repo { get; set; } = string.Empty;
        public List<CatalogApp> Apps { get; set; } = [];
    }

    internal sealed class CatalogApp
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
        public string Commit { get; set; } = string.Empty;
        public string MinServerVersion { get; set; } = "0.0.0";
        public List<string> Permissions { get; set; } = [];
        public List<string> Tags { get; set; } = [];
        public string? Icon { get; set; }
    }
}
