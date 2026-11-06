using System.Text.Json;
using System.Text.RegularExpressions;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// The administrator's list of catalog sources (GitHub repositories), kept as one instance setting. A private
/// repository's read token is protected like any Automaton secret, used only on the server and never returned.
/// </summary>
public sealed partial class CatalogSourceStore(ApplicationDbContext dbContext, ISecretProtector protector)
{
    public const string OfficialId = "official";
    public const string OfficialRepo = "rzvdaniel/automaton-apps";
    public const string DefaultBranch = "main";
    internal const string SettingKey = "Apps.Catalog.Sources";
    private const int MaximumSources = 20;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex RepoPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$")]
    private static partial Regex BranchPattern();

    [GeneratedRegex(@"^https://github\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?/?$")]
    private static partial Regex RepoUrlPattern();

    /// <summary>One source to read: where it is, and the token (decrypted) when it is private.</summary>
    public sealed record SourceRef(string Id, string Name, string Repo, string Branch, bool Official, string? Token);

    public async Task<IReadOnlyList<CatalogSourceDto>> ListAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken);
        var list = new List<CatalogSourceDto> { new(OfficialId, "Official", OfficialRepo, DefaultBranch, true, document.OfficialEnabled, false) };
        list.AddRange(document.Sources.Select(s => new CatalogSourceDto(s.Id.ToString("N"), s.Name, s.Repo, s.Branch, false, s.Enabled, s.ProtectedToken is not null)));
        return list;
    }

    /// <summary>The enabled sources in display order (official first), with their tokens decrypted for server-side use.</summary>
    public async Task<IReadOnlyList<SourceRef>> EnabledAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken);
        var list = new List<SourceRef>();
        if (document.OfficialEnabled) list.Add(new SourceRef(OfficialId, "Official", OfficialRepo, DefaultBranch, true, null));
        foreach (var s in document.Sources.Where(s => s.Enabled))
            list.Add(new SourceRef(s.Id.ToString("N"), s.Name, s.Repo, s.Branch, false, Unprotect(s.ProtectedToken)));
        return list;
    }

    private static bool IsValidToken(string? token) => token is null || (token.Length <= 500 && !token.Any(char.IsWhiteSpace));

    private static bool IsValidBranch(string branch) =>
        branch.Length <= 100 && BranchPattern().IsMatch(branch) && !branch.Contains("..") && !HasDotOnlyPart(branch);

    private static bool HasDotOnlyPart(string path) => path.Split('/').Any(part => part.All(c => c == '.'));

    /// <summary>A candidate source, validated but not saved: the caller checks that it can be read first.</summary>
    public static SourceRef Candidate(string address, string? branch, string? name, string? token)
    {
        var text = (address ?? "").Trim();
        var url = RepoUrlPattern().Match(text);
        var repo = url.Success ? $"{url.Groups[1].Value}/{url.Groups[2].Value}" : text;
        if (!RepoPattern().IsMatch(repo) || HasDotOnlyPart(repo))
            throw new ArgumentException("Enter a GitHub repository as owner/name or https://github.com/owner/name.");
        var branchName = string.IsNullOrWhiteSpace(branch) ? DefaultBranch : branch.Trim();
        if (!IsValidBranch(branchName))
            throw new ArgumentException("That branch name is not valid.");
        var label = string.IsNullOrWhiteSpace(name) ? repo : name.Trim();
        var secret = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        if (label.Length > 100) throw new ArgumentException("The name can be at most 100 characters.");
        if (!IsValidToken(secret)) throw new ArgumentException("That token is not valid.");
        return new SourceRef(Guid.NewGuid().ToString("N"), label, repo, branchName, false, secret);
    }

    /// <summary>Refuses a duplicate or one source too many, so the caller can say so before it reads the repository.</summary>
    public async Task EnsureAddableAsync(SourceRef candidate, CancellationToken cancellationToken) =>
        EnsureAddable(await LoadAsync(cancellationToken), candidate);

    public async Task<CatalogSourceDto> AddAsync(SourceRef candidate, Guid userId, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken);
        EnsureAddable(document, candidate);
        document.Sources.Add(new SourceRecord(Guid.Parse(candidate.Id), candidate.Name, candidate.Repo, candidate.Branch, true,
            candidate.Token is null ? null : protector.Protect(candidate.Token), DateTime.UtcNow, userId));
        await SaveAsync(document, cancellationToken);
        return new CatalogSourceDto(candidate.Id, candidate.Name, candidate.Repo, candidate.Branch, false, true, candidate.Token is not null);
    }

    private static void EnsureAddable(SourcesDocument document, SourceRef candidate)
    {
        if (document.Sources.Count >= MaximumSources)
            throw new ArgumentException($"At most {MaximumSources} custom sources can be added.");
        if (string.Equals(candidate.Repo, OfficialRepo, StringComparison.OrdinalIgnoreCase) && candidate.Branch == DefaultBranch
            || document.Sources.Any(s => string.Equals(s.Repo, candidate.Repo, StringComparison.OrdinalIgnoreCase) && s.Branch == candidate.Branch))
            throw new ArgumentException("That repository is already a catalog source.");
    }

    public async Task<CatalogSourceDto?> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken);
        if (id == OfficialId) document.OfficialEnabled = enabled;
        else
        {
            var index = document.Sources.FindIndex(s => s.Id.ToString("N") == id);
            if (index < 0) return null;
            document.Sources[index] = document.Sources[index] with { Enabled = enabled };
        }
        await SaveAsync(document, cancellationToken);
        return (await ListAsync(cancellationToken)).First(s => s.Id == id);
    }

    /// <summary>Removes a custom source and its saved copy of the catalog. Apps already installed from it are untouched.</summary>
    public async Task<CatalogSourceDto?> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken);
        var index = document.Sources.FindIndex(s => s.Id.ToString("N") == id);
        if (index < 0) return null;
        var removed = document.Sources[index];
        document.Sources.RemoveAt(index);
        await SaveAsync(document, cancellationToken);
        var cache = await dbContext.Settings.FirstOrDefaultAsync(s => s.Key == AppCatalogService.CacheKeyFor(id), cancellationToken);
        if (cache is not null)
        {
            dbContext.Settings.Remove(cache);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return new CatalogSourceDto(id, removed.Name, removed.Repo, removed.Branch, false, removed.Enabled, removed.ProtectedToken is not null);
    }

    private string? Unprotect(string? value)
    {
        if (value is null) return null;
        try { return protector.Unprotect(value); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    private async Task<SourcesDocument> LoadAsync(CancellationToken cancellationToken)
    {
        var setting = await dbContext.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKey, cancellationToken);
        if (setting is null) return new SourcesDocument();
        try { return JsonSerializer.Deserialize<SourcesDocument>(setting.Value, Json) ?? new SourcesDocument(); }
        catch (JsonException) { return new SourcesDocument(); }
    }

    private async Task SaveAsync(SourcesDocument document, CancellationToken cancellationToken)
    {
        var value = JsonSerializer.Serialize(document, Json);
        var setting = await dbContext.Settings.FirstOrDefaultAsync(s => s.Key == SettingKey, cancellationToken);
        if (setting is null)
            dbContext.Settings.Add(new Setting { Key = SettingKey, Value = value, Description = "Catalog sources added by an administrator.", UpdatedAt = DateTime.UtcNow });
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed class SourcesDocument
    {
        public bool OfficialEnabled { get; set; } = true;
        public List<SourceRecord> Sources { get; set; } = [];
    }

    private sealed record SourceRecord(Guid Id, string Name, string Repo, string Branch, bool Enabled, string? ProtectedToken, DateTime Added, Guid AddedBy);
}
