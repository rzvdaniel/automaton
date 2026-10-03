using System.Net.Http.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerExecutionApiClient : IDisposable
{
    private readonly HttpClient client;
    public RunnerExecutionApiClient(Uri address, string apiKey, Guid runnerId) => client = CreateClient(address, apiKey, runnerId);
    internal RunnerExecutionApiClient(HttpClient client) => this.client = client;
    public async Task<HostedRunnerBrokerConfiguration> ConfigurationAsync(CancellationToken token)
        => await GetAsync<HostedRunnerBrokerConfiguration>("api/runner-executions/configuration", token);
    public Task<RunnerExecutionAssignment> AssignmentAsync(Guid id, CancellationToken token)
        => GetAsync<RunnerExecutionAssignment>($"api/runner-executions/{id:D}", token);
    public Task<RunnerExecutionAssignment> StatusAsync(Guid id, CancellationToken token)
        => GetAsync<RunnerExecutionAssignment>($"api/runner-executions/{id:D}/status", token);

    public async Task DownloadInputArtifactAsync(Guid id, string destination, CancellationToken token)
    {
        using var response = await InfrastructureResultDelivery.SendAsync(
            ct => client.GetAsync($"api/runner-executions/{id:D}/input", HttpCompletionOption.ResponseHeadersRead, ct), token);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
            128 * 1024, useAsync: true);
        await source.CopyToAsync(output, token);
        await output.FlushAsync(token);
    }

    public async Task UploadOutputArtifactAsync(Guid id, string path, CancellationToken token)
    {
        using var response = await InfrastructureResultDelivery.SendAsync(async ct =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"api/runner-executions/{id:D}/artifact");
            var content = new StreamContent(File.OpenRead(path));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            request.Content = content;
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }, token);
        response.EnsureSuccessStatusCode();
    }
    private async Task<T> GetAsync<T>(string path, CancellationToken token)
    {
        using var response = await InfrastructureResultDelivery.SendAsync(ct => client.GetAsync(path, ct), token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(token)
            ?? throw new InvalidDataException("Runner execution API returned an empty response.");
    }
    public async Task SubmitAsync(Guid id, RunnerExecutionResult result, CancellationToken token)
    {
        using var response = await InfrastructureResultDelivery.SendAsync(ct =>
            HttpClientJsonExtensions.PostAsJsonAsync(client, $"api/runner-executions/{id:D}/result", result, ct), token);
        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadFromJsonAsync<RunnerExecutionReceipt>(token);
        if (receipt?.Outcome is not ("accepted" or "already-applied"))
            throw new InvalidDataException("Runner execution result was not persisted.");
    }
    private static HttpClient CreateClient(Uri address, string key, Guid runnerId)
    {
        if (address.Scheme != Uri.UriSchemeHttps && !address.IsLoopback)
            throw new InvalidOperationException("Runner execution APIs require HTTPS.");
        var result = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri(address.AbsoluteUri.TrimEnd('/') + "/") };
        result.DefaultRequestHeaders.Add("X-API-Key", key);
        result.DefaultRequestHeaders.Add("X-Runner-Id", runnerId.ToString("D"));
        return result;
    }
    public void Dispose() => client.Dispose();
}
