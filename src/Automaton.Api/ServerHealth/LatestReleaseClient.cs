using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Automaton.Api.ServerHealth;

/// <summary>
/// Finds the newest stable server release the same way the update script does: the tags of the public distribution
/// package. The answer is cached for an hour; null means it could not be determined.
/// </summary>
public sealed partial class LatestReleaseClient(IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
    private const string Registry = "rzvdaniel/automaton-server-distribution";
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);

    public async Task<string?> GetLatestVersionAsync(CancellationToken token)
    {
        if (cache.TryGetValue(nameof(LatestReleaseClient), out string? cached)) return cached;
        string? latest = null;
        try { latest = await FetchAsync(token); }
        catch (Exception exception) when (exception is HttpRequestException or JsonException || exception is TaskCanceledException && !token.IsCancellationRequested)
        {
            // Unreachable registry: report "unknown" and retry sooner.
        }
        cache.Set(nameof(LatestReleaseClient), latest, latest is null ? TimeSpan.FromMinutes(5) : CacheFor);
        return latest;
    }

    private async Task<string?> FetchAsync(CancellationToken token)
    {
        using var client = httpClientFactory.CreateClient(nameof(LatestReleaseClient));
        client.Timeout = TimeSpan.FromSeconds(10);
        using var tokenDocument = JsonDocument.Parse(
            await client.GetStringAsync($"https://ghcr.io/token?scope=repository:{Registry}:pull", token));
        var bearer = tokenDocument.RootElement.GetProperty("token").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://ghcr.io/v2/{Registry}/tags/list?n=10000");
        request.Headers.Authorization = new("Bearer", bearer);
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        using var tags = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return NewestStable(tags.RootElement.GetProperty("tags").EnumerateArray().Select(item => item.GetString()));
    }

    /// <summary>The highest <c>server-vX.Y.Z</c> tag in plain form (X.Y.Z).</summary>
    public static string? NewestStable(IEnumerable<string?> tags)
        => tags.Where(tag => tag is not null && StableTag().IsMatch(tag))
            .Select(tag => Version.Parse(tag!["server-v".Length..]))
            .OrderDescending().FirstOrDefault()?.ToString();

    [GeneratedRegex(@"^server-v\d+\.\d+\.\d+$")]
    private static partial Regex StableTag();
}
