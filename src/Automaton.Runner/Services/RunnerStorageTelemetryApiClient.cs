using System.Net.Http.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

public sealed class RunnerStorageTelemetryApiClient(
    ConfigurationService configuration,
    FileStorageService storage,
    IHttpClientFactory httpClientFactory)
{
    public Task ReportHostedRunnerAsync(
        HostedRunnerStorageTelemetry report,
        CancellationToken cancellationToken)
        => PostAsync("api/runner-storage/hosted", report, cancellationToken);

    public Task ReportRunnerHostAsync(
        RunnerHostStorageTelemetry report,
        CancellationToken cancellationToken)
        => PostAsync("api/runner-storage/host", report, cancellationToken);

    private async Task PostAsync<TReport>(
        string path,
        TReport report,
        CancellationToken cancellationToken)
    {
        var apiUrl = ResolveApiUrl(configuration.BaseUrl);
        var apiKey = await storage.LoadApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("The Runner API key is not available for storage telemetry.");

        using var client = httpClientFactory.CreateClient(nameof(RunnerStorageTelemetryApiClient));
        client.BaseAddress = new Uri(apiUrl.AbsoluteUri.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        using var content = JsonContent.Create(report);
        using var response = await client.PostAsync(path, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static Uri ResolveApiUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var apiUrl)
            || (apiUrl.Scheme != Uri.UriSchemeHttps
                && (apiUrl.Scheme != Uri.UriSchemeHttp || !apiUrl.IsLoopback)))
            throw new InvalidOperationException("Runner storage telemetry requires an HTTPS API URL.");
        return apiUrl;
    }
}
