using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Automaton.Infrastructure.Providers;
using Automaton.Worker.Config;
using Microsoft.Extensions.Options;

namespace Automaton.Worker.Services;

public sealed class HetznerInfrastructureProvider(
    HttpClient httpClient,
    IOptions<HetznerInfrastructureProviderOptions> options,
    ILogger<HetznerInfrastructureProvider> logger) : IRunnerHostInfrastructureProvider
{
    private readonly HetznerInfrastructureProviderOptions settings = options.Value;
    public string ProviderName => "hetzner";

    public async Task<CreateRunnerHostServerResult> CreateServerAsync(CreateRunnerHostServerRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var payload = new
        {
            name = request.Name,
            server_type = new { name = request.ServerType },
            image = new { name = request.Image },
            location = new { name = request.Region },
            ssh_keys = request.SshKeyIds.Select(long.Parse).ToArray(),
            user_data = request.CloudInit,
            networks = string.IsNullOrWhiteSpace(request.PrivateNetworkId) ? null : new[] { long.Parse(request.PrivateNetworkId) },
            labels = request.Labels
        };
        using var response = await SendAsync(HttpMethod.Post, "/servers", JsonContent.Create(payload), cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return ReadCreateResult(document.RootElement.GetProperty("server"));
    }

    public async Task<RunnerHostServerInfo?> GetServerAsync(string providerResourceId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await SendAsync(HttpMethod.Get, $"/servers/{Uri.EscapeDataString(providerResourceId)}", null, cancellationToken, true);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return ReadServerInfo(document.RootElement.GetProperty("server"));
    }

    public async Task RebootServerAsync(string providerResourceId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await SendAsync(HttpMethod.Post, $"/servers/{Uri.EscapeDataString(providerResourceId)}/actions/reboot", JsonContent.Create(new { }), cancellationToken);
    }

    public async Task DeleteServerAsync(string providerResourceId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await SendAsync(HttpMethod.Delete, $"/servers/{Uri.EscapeDataString(providerResourceId)}", null, cancellationToken, true);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri(settings.ApiBaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'))) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);
        request.Headers.UserAgent.ParseAdd("Automaton-Worker/runner-host-provisioner");
        request.Headers.Add("X-Automaton-Correlation-Id", Guid.NewGuid().ToString("N"));
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)) return response;
        var status = response.StatusCode;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        response.Dispose();
        logger.LogWarning("Hetzner provider request failed with status {StatusCode}: {Detail}", status, detail.Length > 1000 ? detail[..1000] : detail);
        throw new HttpRequestException($"Hetzner API request failed with status {(int)status}.");
    }

    private void EnsureConfigured()
    {
        if (!settings.Enabled) throw new InvalidOperationException("The Hetzner infrastructure provider is disabled.");
        if (string.IsNullOrWhiteSpace(settings.ApiToken)) throw new InvalidOperationException("InfrastructureProviders:Hetzner:ApiToken is required.");
    }

    private static CreateRunnerHostServerResult ReadCreateResult(JsonElement server)
    {
        var info = ReadServerInfo(server);
        return new(info.ProviderResourceId, info.PublicIp, info.PrivateIp, info.ProviderRegion, info.ProviderServerType, info.ProviderImageId);
    }

    private static RunnerHostServerInfo ReadServerInfo(JsonElement server)
    {
        var publicIp = server.TryGetProperty("public_net", out var publicNet) && publicNet.TryGetProperty("ipv4", out var ipv4) ? ipv4.GetProperty("ip").GetString() : null;
        var privateIp = server.TryGetProperty("private_net", out var privateNet) && privateNet.ValueKind == JsonValueKind.Array && privateNet.GetArrayLength() > 0 ? privateNet[0].GetProperty("ip").GetString() : null;
        var serverType = server.TryGetProperty("server_type", out var type) && type.TryGetProperty("name", out var typeName) ? typeName.GetString() : null;
        var image = server.TryGetProperty("image", out var imageElement) && imageElement.TryGetProperty("name", out var imageName) ? imageName.GetString() : null;
        var location = server.TryGetProperty("datacenter", out var datacenter) && datacenter.TryGetProperty("location", out var locationElement) && locationElement.TryGetProperty("name", out var locationName) ? locationName.GetString() : null;
        return new(server.GetProperty("id").GetInt64().ToString(), server.GetProperty("status").GetString() ?? "unknown", publicIp, privateIp, location, serverType, image);
    }
}
