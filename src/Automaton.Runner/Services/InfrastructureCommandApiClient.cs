using System.Net.Http.Json;
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
    FileStorageService storage) : IInfrastructureSecretProvider, IInfrastructureCommandResultClient
{
    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken);
        using var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, $"api/infrastructure-commands/{command.CommandId}/secrets",
            new InfrastructureCommandSecretsRequest(command.OperationId, command.RunnerHostId,
                command.InfrastructureRunnerId, command.ActionKey, command.SecretGrantIds), cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<InfrastructureCommandSecretsResponse>(cancellationToken);
        return body?.EnvironmentVariables ?? throw new InvalidOperationException("The API returned no infrastructure command secrets.");
    }

    public async Task SubmitAsync(InfrastructureCommandRequestedEvent command, AppActionExecutionResult execution,
        AppModuleExecutionArtifactResponse result, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken);
        using var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, $"api/infrastructure-commands/{command.CommandId}/result",
            new InfrastructureCommandResultRequest(command.OperationId, command.RunnerHostId,
                command.InfrastructureRunnerId, command.ActionKey, command.SecretGrantIds, execution, result), cancellationToken);
        response.EnsureSuccessStatusCode();
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
        var client = new HttpClient { BaseAddress = new Uri(address.ToString().TrimEnd('/') + "/") };
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        return client;
    }

    private sealed record InfrastructureCommandSecretsRequest(Guid OperationId, Guid RunnerHostId,
        Guid InfrastructureRunnerId, string ActionKey, IReadOnlyList<Guid> SecretGrantIds);
    private sealed record InfrastructureCommandSecretsResponse(IReadOnlyDictionary<string, string> EnvironmentVariables);
    private sealed record InfrastructureCommandResultRequest(Guid OperationId, Guid RunnerHostId,
        Guid InfrastructureRunnerId, string ActionKey, IReadOnlyList<Guid> SecretGrantIds,
        AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result);
}
