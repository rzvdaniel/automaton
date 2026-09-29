using System.Net.Http.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class HostedRunnerCommandApiClient
{
    private readonly Uri apiUrl;
    private readonly string apiKey;

    public HostedRunnerCommandApiClient(Uri apiUrl, string apiKey)
    {
        ApiUrl = apiUrl;
        ApiKey = apiKey;
        this.apiUrl = apiUrl;
        this.apiKey = apiKey;
    }

    public Uri ApiUrl { get; }
    public string ApiKey { get; }

    public async Task<HostedRunnerBrokerConfiguration> LoadBrokerConfigurationAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("api/hosted-runner-commands/configuration", cancellationToken);
        response.EnsureSuccessStatusCode();
        var broker = await response.Content.ReadFromJsonAsync<HostedRunnerBrokerConfiguration>(cancellationToken);
        return broker ?? throw new InvalidOperationException("The API returned no Hosted Runner broker configuration.");
    }

    public async Task<string> GetCommandDispositionAsync(
        HostedRunnerDeploymentCommand command,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var httpResponse = await client.GetAsync(
            $"api/hosted-runner-commands/operations/{command.OperationId:D}/status?attemptId={command.ExecutionAttemptId:D}",
            cancellationToken);
        httpResponse.EnsureSuccessStatusCode();
        var response = await httpResponse.Content.ReadFromJsonAsync<CommandResultResponse>(cancellationToken);
        return response?.Outcome ?? throw new InvalidOperationException("The API returned no command disposition.");
    }

    public async Task<string> SubmitResultAsync(
        HostedRunnerDeploymentCommand command,
        DeployAppPackageResult result,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(
            client,
            $"api/hosted-runner-commands/operations/{command.OperationId:D}/result",
            new HostedRunnerDeploymentResultRequest(command.ExecutionAttemptId, result),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var outcome = await response.Content.ReadFromJsonAsync<CommandResultResponse>(cancellationToken);
        if (outcome?.Outcome is not ("accepted" or "already-applied" or "stale" or "obsolete"))
            throw new InvalidOperationException("The API did not persist this Hosted Runner deployment result.");
        return outcome.Outcome;
    }

    public async Task<string> RejectCommandAsync(
        Guid operationId,
        Guid executionAttemptId,
        string reason,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(
            client,
            $"api/hosted-runner-commands/operations/{operationId:D}/reject",
            new HostedRunnerDeploymentCommandRejectionRequest(executionAttemptId, reason),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var outcome = await response.Content.ReadFromJsonAsync<CommandResultResponse>(cancellationToken);
        if (outcome?.Outcome is not ("accepted" or "already-applied" or "stale" or "obsolete"))
            throw new InvalidOperationException("The API did not persist the Hosted Runner command rejection.");
        return outcome.Outcome;
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(apiUrl.AbsoluteUri.TrimEnd('/') + "/")
        };
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        return client;
    }

    private sealed record CommandResultResponse(string Outcome);
}
