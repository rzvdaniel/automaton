using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Cli.Services;

/// <summary>
/// Thin HTTP client wrapper over the Automaton REST API.
/// All methods throw on non-success status codes.
/// </summary>
public class AutomatonApiClient : IDisposable
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive  = true,
        PropertyNamingPolicy         = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition       = JsonIgnoreCondition.WhenWritingNull
    };

    public AutomatonApiClient(string serverUrl, string apiKey)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/"),
            Timeout     = TimeSpan.FromMinutes(5)
        };
        _http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }

    // ── Apps ──────────────────────────────────────────────────────────────────

    public async Task<(string appId, string name)> CreateAppAsync(string name, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { name }, JsonOptions);
        using var resp = await PostJsonAsync("api/apps", payload, ct);
        await EnsureSuccessAsync(resp);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var appId  = root.GetProperty("id").GetString()!;
        var appName = root.TryGetProperty("name", out var n) ? n.GetString()! : name;
        return (appId, appName);
    }

    public async Task<JsonElement> GetAppAsync(string appId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/apps/{appId}", ct);
        await EnsureSuccessAsync(resp);
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // ── File upload ───────────────────────────────────────────────────────────

    /// <summary>
    /// Upload a zip archive to the app. clean=true wipes existing files first.
    /// </summary>
    public async Task<JsonElement> UploadZipAsync(string appId, Stream zipStream, bool clean = false, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(zipStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(streamContent, "file", "app.zip");

        var url = $"api/apps/{appId}/upload{(clean ? "?clean=true" : "")}";
        using var resp = await _http.PostAsync(url, content, ct);
        await EnsureSuccessAsync(resp);
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // ── Backend deploy ────────────────────────────────────────────────────────

    public async Task<JsonElement> DeployBackendAsync(string appId, CancellationToken ct = default)
    {
        using var resp = await PostJsonAsync($"api/apps/{appId}/backend/deploy", "{}", ct);
        await EnsureSuccessAsync(resp);
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // ── Publish (version snapshot) ────────────────────────────────────────────

    public async Task<JsonElement> PublishAsync(string appId, string? summary = null, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { summary, trigger = "ManualSave" }, JsonOptions);
        using var resp = await PostJsonAsync($"api/apps/{appId}/versions/snapshot", payload, ct);
        await EnsureSuccessAsync(resp);
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // ── Runners ───────────────────────────────────────────────────────────────

    public async Task<JsonElement> ListRunnersAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("api/runners", ct);
        await EnsureSuccessAsync(resp);
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // ── App files ─────────────────────────────────────────────────────────────

    public async Task<JsonElement> ListFilesAsync(string appId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/apps/{appId}/files", ct);
        await EnsureSuccessAsync(resp);
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    public async Task<string> ReadFileAsync(string appId, string path, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(path);
        using var resp = await _http.GetAsync($"api/apps/{appId}/files/{encoded}", ct);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadAsStringAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> PostJsonAsync(string url, string json, CancellationToken ct)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.PostAsync(url, content, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"API error {(int)resp.StatusCode} {resp.ReasonPhrase}: {body}");
        }
    }

    public void Dispose() => _http.Dispose();
}
