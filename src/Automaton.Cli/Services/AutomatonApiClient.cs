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
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> GetUserProfileAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("api/User/GetUserProfile", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task UpdateAppRunnerAsync(string appId, string? runnerId, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { executionRunnerId = runnerId }, JsonOptions);
        using var resp = await PutJsonAsync($"api/apps/{appId}/runner", payload, ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task<JsonElement> CreateApiKeyAsync(string name, string? description, int scope, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { name, description, scope }, JsonOptions);
        using var resp = await PostJsonAsync("api/ApiKeys", payload, ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
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
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> UploadBackendZipAsync(string appId, Stream zipStream, bool clean = true, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(zipStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(streamContent, "file", "backend.zip");

        var url = $"api/apps/{appId}/backend/upload{(clean ? "?clean=true" : "")}";
        using var resp = await _http.PostAsync(url, content, ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    // ── Backend deploy ────────────────────────────────────────────────────────

    public async Task<JsonElement> DeployBackendAsync(string appId, CancellationToken ct = default)
    {
        using var resp = await PostJsonAsync($"api/apps/{appId}/backend/deploy", "{}", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    // ── Publish (version snapshot) ────────────────────────────────────────────

    public async Task<JsonElement> PublishAsync(string appId, string? summary = null, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { summary, trigger = "ManualSave" }, JsonOptions);
        using var resp = await PostJsonAsync($"api/apps/{appId}/versions/snapshot", payload, ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    // ── Runners ───────────────────────────────────────────────────────────────

    public async Task<JsonElement> ListRunnersAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("api/runners", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    // ── App files ─────────────────────────────────────────────────────────────

    public async Task<JsonElement> ListFilesAsync(string appId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/apps/{appId}/files", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<string> ReadFileAsync(string appId, string path, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(path);
        using var resp = await _http.GetAsync($"api/apps/{appId}/files/{encoded}", ct);
        await EnsureSuccessAsync(resp);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return UnwrapFileContent(body);
    }

    private static string UnwrapFileContent(string body)
    {
        const int maxEnvelopeDepth = 5;
        var current = body;

        for (var depth = 0; depth < maxEnvelopeDepth; depth++)
        {
            try
            {
                using var doc = JsonDocument.Parse(current);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("success", out var successProperty)
                    || (successProperty.ValueKind != JsonValueKind.True && successProperty.ValueKind != JsonValueKind.False)
                    || !root.TryGetProperty("content", out var contentProperty)
                    || contentProperty.ValueKind != JsonValueKind.String)
                {
                    return current;
                }

                current = contentProperty.GetString() ?? string.Empty;
            }
            catch (JsonException)
            {
                // Raw file bodies are not JSON and should be returned unchanged.
                return current;
            }
        }

        return current;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> PostJsonAsync(string url, string json, CancellationToken ct)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.PostAsync(url, content, ct);
    }

    private async Task<HttpResponseMessage> PutJsonAsync(string url, string json, CancellationToken ct)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.PutAsync(url, content, ct);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);

        try
        {
            return JsonDocument.Parse(body).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            var mediaType = resp.Content.Headers.ContentType?.MediaType;
            var preview = body.Length > 160 ? body[..160] + "..." : body;
            var looksLikeHtml = preview.TrimStart().StartsWith("<", StringComparison.Ordinal);
            var formatHint = looksLikeHtml || string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                ? "The server returned HTML instead of the Automaton API JSON response. Verify you are using the API base URL, for example https://api.automaton.monster."
                : "The server returned a non-JSON response.";

            throw new InvalidOperationException(
                $"Unexpected response from {resp.RequestMessage?.RequestUri}. {formatHint} Content-Type: {mediaType ?? "unknown"}. Response preview: {preview}",
                ex);
        }
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
