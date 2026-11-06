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
    ILogger<AppCatalogService> logger)
{
    public const string DefaultSourceUrl = "https://raw.githubusercontent.com/rzvdaniel/automaton-apps/main/catalog.json";
    internal const string CacheKey = "Apps.Catalog.Cache";
    internal const int SupportedSchemaVersion = 1;
    private const long MaximumBytes = 1024 * 1024;
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CatalogResponse> GetAsync(Guid userId, bool refresh, CancellationToken cancellationToken)
    {
        var cached = await LoadCacheAsync(cancellationToken);
        string? failure = null;
        // A forced refresh still waits out a short pause, so a signed-in user cannot make the server hammer GitHub.
        var age = cached is null ? TimeSpan.MaxValue : DateTime.UtcNow - cached.FetchedAt;
        if (cached is null || age > FreshFor || (refresh && age > TimeSpan.FromSeconds(configuration.GetValue("Catalog:MinimumRefreshSeconds", 30))))
        {
            var (updated, error) = await FetchAsync(cached, cancellationToken);
            failure = error;
            cached = updated ?? cached;
        }

        if (cached is null)
            return new CatalogResponse([], null, false, true, failure ?? "The catalog could not be loaded.");

        var installed = await InstalledAsync(userId, cancellationToken);
        var version = ParseVersion(configuration["Installation:Version"]);
        var apps = cached.Document.Apps.Select(app => ToDto(app, cached.Document.Repo, installed, version)).ToList();
        return new CatalogResponse(apps, cached.FetchedAt, failure is not null, false,
            failure is null ? null : $"{failure} Showing the list from {cached.FetchedAt:u}.");
    }

    private async Task<(CacheEntry? Updated, string? Error)> FetchAsync(CacheEntry? cached, CancellationToken cancellationToken)
    {
        var url = configuration["Catalog:SourceUrl"] is { Length: > 0 } configured ? configured : DefaultSourceUrl;
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Automaton-Server");
            if (cached?.ETag is { Length: > 0 } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cached is not null)
            {
                var touched = cached with { FetchedAt = DateTime.UtcNow };
                await SaveCacheAsync(touched, cancellationToken);
                return (touched, null);
            }

            if (!response.IsSuccessStatusCode)
                return (null, $"The catalog source answered {(int)response.StatusCode}.");

            var body = await ReadBoundedAsync(response, cancellationToken);
            var document = Parse(body);
            var entry = new CacheEntry(DateTime.UtcNow, response.Headers.ETag?.ToString(), body, document);
            await SaveCacheAsync(entry, cancellationToken);
            return (entry, null);
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

    internal static CatalogDocument Parse(string body)
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(body, Json)
            ?? throw new InvalidDataException("Empty catalog.");
        if (document.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException("Unsupported catalog schema version.");
        if (string.IsNullOrWhiteSpace(document.Repo))
            throw new InvalidDataException("The catalog has no repository.");
        // One bad entry must not hide the others.
        document.Apps.RemoveAll(app => !AppCatalogInstallService.IsValidSource(document.Repo, app.Folder, app.Commit));
        return document;
    }

    private async Task<CacheEntry?> LoadCacheAsync(CancellationToken cancellationToken)
    {
        var setting = await dbContext.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == CacheKey, cancellationToken);
        if (setting is null) return null;
        try
        {
            var stored = JsonSerializer.Deserialize<StoredCache>(setting.Value, Json);
            return stored is null ? null : new CacheEntry(stored.FetchedAt, stored.ETag, stored.Body, Parse(stored.Body));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private async Task SaveCacheAsync(CacheEntry entry, CancellationToken cancellationToken)
    {
        var value = JsonSerializer.Serialize(new StoredCache(entry.FetchedAt, entry.ETag, entry.Body), Json);
        var setting = await dbContext.Settings.FirstOrDefaultAsync(s => s.Key == CacheKey, cancellationToken);
        if (setting is null)
            dbContext.Settings.Add(new Setting { Key = CacheKey, Value = value, Description = "Last good copy of the app catalog.", UpdatedAt = DateTime.UtcNow });
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
            foreach (var entry in dbContext.ChangeTracker.Entries<Setting>().Where(e => e.Entity.Key == CacheKey).ToList())
                entry.State = EntityState.Detached;
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

    private static CatalogEntryDto ToDto(CatalogApp app, string repo, Dictionary<(string Repo, string Folder), Guid> installed, Version? serverVersion)
    {
        var minimum = ParseVersion(app.MinServerVersion);
        var needsNewer = serverVersion is not null && minimum is not null && serverVersion < minimum;
        Guid? installedId = installed.TryGetValue((repo, app.Folder), out var id) ? id : null;
        return new CatalogEntryDto(app.Id, app.Name, app.Summary, app.Category, repo, app.Folder, app.Commit,
            app.MinServerVersion, app.Permissions, needsNewer, installedId);
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
    }
}
