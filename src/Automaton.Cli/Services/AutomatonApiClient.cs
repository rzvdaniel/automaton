using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.AiGuidance;
using Automaton.Cli.Models;

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

    public AutomatonApiClient(string serverUrl, string? apiKey = null)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/"),
            Timeout     = TimeSpan.FromMinutes(5)
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }
    }

    public AutomatonApiClient(HttpClient httpClient, string? apiKey = null)
    {
        _http = httpClient;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }
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

    public async Task<IReadOnlyList<DeploymentTargetInfo>> ListDeploymentTargetsAsync(
        string appId,
        CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/apps/{appId}/targets", ct);
        await EnsureSuccessAsync(resp);
        return JsonSerializer.Deserialize<List<DeploymentTargetInfo>>(
            await resp.Content.ReadAsStringAsync(ct),
            JsonOptions) ?? [];
    }

    public async Task<DeploymentTargetInfo> CreateDeploymentTargetAsync(
        string appId,
        string name,
        string slug,
        string? runnerId,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { name, slug, runnerId }, JsonOptions);
        using var resp = await PostJsonAsync($"api/apps/{appId}/targets", payload, ct);
        await EnsureSuccessAsync(resp);
        return JsonSerializer.Deserialize<DeploymentTargetInfo>(
            await resp.Content.ReadAsStringAsync(ct),
            JsonOptions) ?? throw new InvalidOperationException("Target response was empty.");
    }

    public async Task MakeDeploymentTargetDefaultAsync(
        string appId,
        string targetId,
        CancellationToken ct = default)
    {
        using var resp = await PostJsonAsync(
            $"api/apps/{appId}/targets/{targetId}/make-default", "{}", ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task AssignDeploymentTargetRunnerAsync(
        string appId,
        string targetId,
        string? runnerId,
        bool cleanupDeployedRuntime = false,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(
            new { runnerId, cleanupDeployedRuntime },
            JsonOptions);
        using var resp = await PostJsonAsync(
            $"api/apps/{appId}/targets/{targetId}/assign-runner", payload, ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task ApproveDeploymentTargetPrivilegedHostAccessAsync(
        string appId,
        string targetId,
        CancellationToken ct = default)
    {
        using var resp = await PostJsonAsync(
            $"api/apps/{appId}/targets/{targetId}/privileged-host-access", "{}", ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task RevokeDeploymentTargetPrivilegedHostAccessAsync(
        string appId,
        string targetId,
        CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync(
            $"api/apps/{appId}/targets/{targetId}/privileged-host-access", ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task DeleteDeploymentTargetAsync(
        string appId,
        string targetId,
        CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync(
            $"api/apps/{appId}/targets/{targetId}", ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task<JsonElement> GetUserProfileAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("api/User/GetUserProfile", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> ListAppsAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("api/apps", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> RunAppActionAsync(string appId, string actionKey, JsonElement input, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { input }, JsonOptions);
        using var resp = await PostJsonAsync($"api/apps/{appId}/actions/{Uri.EscapeDataString(actionKey)}/executions", payload, ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> GetAppActionExecutionAsync(string appId, Guid executionId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/apps/{appId}/action-executions/{executionId}", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> GetAppActionArtifactAsync(string appId, Guid executionId, bool result, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/apps/{appId}/action-executions/{executionId}/{(result ? "result" : "logs")}", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> CancelAppActionExecutionAsync(string appId, Guid executionId, CancellationToken ct = default)
    {
        using var resp = await PostJsonAsync($"api/apps/{appId}/action-executions/{executionId}/cancel", "{}", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> ListAppActionVersionsAsync(string appId, string actionKey, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/apps/{appId}/actions/{Uri.EscapeDataString(actionKey)}/versions", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> RestoreAppActionVersionAsync(string appId, string actionKey, string version, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { version }, JsonOptions);
        using var resp = await PostJsonAsync($"api/apps/{appId}/actions/{Uri.EscapeDataString(actionKey)}/rollback", payload, ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> DeleteAppActionVersionAsync(string appId, string actionKey, string version, CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync($"api/apps/{appId}/actions/{Uri.EscapeDataString(actionKey)}/versions/{Uri.EscapeDataString(version)}", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<string> CreateRuntimeLaunchAsync(string appId, CancellationToken ct = default)
    {
        using var resp = await PostJsonAsync($"api/apps/{appId}/runtime-launch", "{}", ct);
        await EnsureSuccessAsync(resp);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("runtimeToken").GetString()
            ?? throw new InvalidOperationException("Runtime launch response did not include a runtimeToken.");
    }

    public async Task<JsonElement> CreateApiKeyAsync(string name, string? description, int scope, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { name, description, scope }, JsonOptions);
        using var resp = await PostJsonAsync("api/ApiKeys", payload, ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<CliAuthorizationResponse> CreateCliAuthorizationRequestAsync(string deviceName, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { deviceName }, JsonOptions);
        using var resp = await PostJsonAsync("api/cli-auth/requests", payload, ct);
        await EnsureSuccessAsync(resp);
        var json = await ReadJsonAsync(resp, ct);
        return new CliAuthorizationResponse(
            json.GetProperty("requestId").GetGuid(),
            json.GetProperty("userCode").GetString()!,
            json.GetProperty("verifier").GetString()!,
            json.GetProperty("verificationUrl").GetString()!,
            json.GetProperty("expiresIn").GetInt32(),
            json.GetProperty("pollInterval").GetInt32());
    }

    public async Task<JsonElement> PollCliAuthorizationAsync(Guid requestId, string verifier, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { verifier }, JsonOptions);
        using var resp = await PostJsonAsync($"api/cli-auth/requests/{requestId}/poll", payload, ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task CompleteCliAuthorizationAsync(Guid requestId, string verifier, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { verifier }, JsonOptions);
        using var resp = await PostJsonAsync($"api/cli-auth/requests/{requestId}/complete", payload, ct);
        await EnsureSuccessAsync(resp);
    }

    // ── File upload ───────────────────────────────────────────────────────────

    /// <summary>
    /// Upload a zip archive to the app. clean=true wipes existing files first.
    /// </summary>
    public async Task<JsonElement> DeployPackageAsync(
        string appId,
        Stream zipStream,
        bool clean = false,
        string? targetSlug = null,
        CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(zipStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(streamContent, "file", "deploy-package.zip");

        var query = new List<string>();
        if (clean)
        {
            query.Add("clean=true");
        }
        if (!string.IsNullOrWhiteSpace(targetSlug))
        {
            query.Add($"targetSlug={Uri.EscapeDataString(targetSlug)}");
        }
        var url = $"api/apps/{appId}/deploy-package{(query.Count > 0 ? $"?{string.Join("&", query)}" : "")}";
        var correlationId = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("X-Automaton-Correlation-Id", correlationId);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new CliHttpRequestException(
                BuildUploadDiagnostic(_http.BaseAddress, url, GetPackageBytes(zipStream), correlationId, ex),
                correlationId,
                ex);
        }

        using (resp)
        {
            await EnsureSuccessAsync(resp);
            var accepted = await ReadJsonAsync(resp, ct);
            if (!accepted.TryGetProperty("operationId", out var operationIdProperty)
                || !Guid.TryParse(operationIdProperty.GetString(), out var operationId))
            {
                throw new InvalidOperationException("Deploy package response did not contain an operation ID.");
            }

            while (true)
            {
                using var statusResponse = await _http.GetAsync(
                    $"api/apps/{appId}/deploy-operations/{operationId}",
                    ct);
                await EnsureSuccessAsync(statusResponse);
                var status = await ReadJsonAsync(statusResponse, ct);
                var operationStatus = status.TryGetProperty("status", out var statusProperty)
                    ? statusProperty.GetString()
                    : null;
                if (string.Equals(operationStatus, "Succeeded", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(operationStatus, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    return status.TryGetProperty("result", out var result)
                        && result.ValueKind == JsonValueKind.Object
                        ? result.Clone()
                        : status;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }

    private static long? GetPackageBytes(Stream stream)
    {
        try
        {
            return stream.CanSeek ? stream.Length - stream.Position : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string BuildUploadDiagnostic(
        Uri? baseAddress,
        string url,
        long? packageBytes,
        string correlationId,
        Exception exception)
    {
        var requestUri = baseAddress is null
            ? new Uri(url, UriKind.RelativeOrAbsolute)
            : new Uri(baseAddress, url);
        var category = exception switch
        {
            TaskCanceledException => "Timeout",
            _ when ContainsException<AuthenticationException>(exception)
                => "SecureConnectionError",
            _ when ContainsException<SocketException>(exception)
                || ContainsException<IOException>(exception) => "ConnectionError",
            _ => "ConnectionError"
        };
        var bytes = packageBytes.HasValue ? packageBytes.Value.ToString() : "unknown";
        var chain = string.Join(" -> ", ExceptionChain(exception));

        return $"Phase: deploy-package HTTP upload; "
            + $"Request host/path: {requestUri.Host}{requestUri.AbsolutePath}{requestUri.Query}; "
            + $"Package bytes: {bytes}; "
            + $"HTTP request error category: {category}; "
            + $"Exception chain: {chain}; "
            + $"Correlation ID: {correlationId}";
    }

    private static IEnumerable<string> ExceptionChain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current is SocketException socket
                ? $"{current.GetType().Name} {socket.SocketErrorCode}"
                : current.GetType().Name;
        }
    }

    private static bool ContainsException<TException>(Exception exception)
        where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<JsonElement> GetManagedRuntimeReadinessAsync(string appId, CancellationToken ct = default)
    {
        using var resp = await PostJsonAsync($"api/apps/{appId}/managed-runtime/readiness", "{}", ct);
        await EnsureSuccessAsync(resp);
        return await ReadJsonAsync(resp, ct);
    }

    // ── Publish app / versioning ──────────────────────────────────────────────

    public async Task PublishAppAsync(
        string appId,
        string publicUrl,
        bool requiresAuthentication = false,
        string? category = null,
        string? tags = null,
        bool isForkable = false,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            publicUrl,
            requiresAuthentication,
            category,
            tags,
            isForkable
        }, JsonOptions);
        using var resp = await PostJsonAsync($"api/apps/{appId}/publish", payload, ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task<JsonElement> CreateVersionSnapshotAsync(
        string appId,
        string? trigger = null,
        string? summary = null,
        string? source = null,
        string? reason = null,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { summary, trigger = trigger ?? "ManualSave", source, reason }, JsonOptions);
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

    public async Task<JsonElement> ListScopedFilesAsync(string appId, string scope, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/apps/{appId}/filesystem?scope={Uri.EscapeDataString(scope)}", ct);
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

    public async Task<string> ReadScopedFileAsync(string appId, string scope, string path, CancellationToken ct = default)
    {
        var encodedScope = Uri.EscapeDataString(scope);
        var encodedPath = Uri.EscapeDataString(path);
        using var resp = await _http.GetAsync($"api/apps/{appId}/filesystem/{encodedScope}/{encodedPath}", ct);
        await EnsureSuccessAsync(resp);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return UnwrapFileContent(body);
    }

    public async Task<IReadOnlyList<GuidanceSource>> GetAiGuidanceExportAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("api/ai-guidance/export", ct);
        await EnsureSuccessAsync(resp);
        var json = await ReadJsonAsync(resp, ct);
        var files = new List<GuidanceSource>();

        if (json.TryGetProperty("files", out var filesProperty) && filesProperty.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in filesProperty.EnumerateArray())
            {
                var outputPath = item.TryGetProperty("outputPath", out var outputPathProperty)
                    ? outputPathProperty.GetString()
                    : null;
                var content = item.TryGetProperty("content", out var contentProperty)
                    ? contentProperty.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(outputPath) && content is not null)
                {
                    files.Add(new GuidanceSource(outputPath, content));
                }
            }
        }

        return files;
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

public sealed class CliHttpRequestException : HttpRequestException
{
    public CliHttpRequestException(string message, string correlationId, Exception innerException)
        : base(message, innerException) => CorrelationId = correlationId;

    public string CorrelationId { get; }
}

public sealed record CliAuthorizationResponse(
    Guid RequestId,
    string UserCode,
    string Verifier,
    string VerificationUrl,
    int ExpiresIn,
    int PollInterval);
