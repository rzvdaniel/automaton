using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging.Events;
using Microsoft.Extensions.Configuration;

namespace Automaton.Runner.Console.Services;

public interface IInfrastructureCommandResultClient
{
    Task SubmitAsync(InfrastructureCommandRequestedEvent command, AppActionExecutionResult execution,
        AppModuleExecutionArtifactResponse result, CancellationToken cancellationToken);
}

public sealed class InfrastructureCommandApiClient(
    IConfiguration configuration,
    FileStorageService storage) : IInfrastructureCommandResultClient
{
    public async Task SubmitAsync(InfrastructureCommandRequestedEvent command, AppActionExecutionResult execution,
        AppModuleExecutionArtifactResponse result, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken);
        using var response = await InfrastructureResultDelivery.SendAsync(token => HttpClientJsonExtensions.PostAsJsonAsync(client, $"api/infrastructure-commands/{command.CommandId}/result",
            new InfrastructureCommandResultRequest(command.OperationId, command.RunnerHostId,
                command.InfrastructureRunnerId, command.ActionKey, command.SecretGrantIds, execution, result), token), cancellationToken);
        response.EnsureSuccessStatusCode();
        var outcome = await response.Content.ReadFromJsonAsync<InfrastructureCommandResultResponse>(cancellationToken);
        if (!IsAckableResponse(response.StatusCode, outcome))
            throw new InvalidOperationException("The Infrastructure API returned no authoritative terminal result outcome.");
    }

    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var apiKey = await storage.LoadApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("An Infrastructure Runner API key is required for infrastructure commands.");
        var baseUrl = Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL")
            ?? await storage.LoadServerUrlAsync()
            ?? configuration["AppConfig:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address))
            throw new InvalidOperationException("The Infrastructure Runner API server URL is required for infrastructure commands.");
        if (address.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Infrastructure command APIs require an HTTPS server URL.");
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(address.ToString().TrimEnd('/') + "/")
        };
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        return client;
    }

    internal static bool IsAckableResponse(HttpStatusCode statusCode, InfrastructureCommandResultResponse? response)
        => ((int)statusCode is >= 200 and < 300)
            && response != null
            && InfrastructureCommandResultOutcomes.IsAckable(response.Outcome);

    private sealed record InfrastructureCommandResultRequest(Guid OperationId, Guid RunnerHostId,
        Guid InfrastructureRunnerId, string ActionKey, IReadOnlyList<Guid> SecretGrantIds,
        AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result);
}
