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

public interface IHostContainerExecutionCommandClient
{
    Task<string> GetDispositionAsync(Guid operationId, CancellationToken cancellationToken);
    Task SubmitAsync(InfrastructureCommandRequestedEvent command, HostContainerExecutionResponse result,
        CancellationToken cancellationToken);
}

public sealed class InfrastructureCommandApiClient(
    IConfiguration configuration,
    FileStorageService storage) : IInfrastructureSecretProvider, IInfrastructureCommandResultClient,
    IHostContainerExecutionCommandClient
{
    public async Task<InfrastructureCommandSecretsResponse> ResolveAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken);
        using var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, $"api/infrastructure-commands/{command.CommandId}/secrets",
            new InfrastructureCommandSecretsRequest(command.OperationId, command.RunnerHostId,
                command.InfrastructureRunnerId, command.ActionKey, command.SecretGrantIds), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            Serilog.Log.Warning(
                "Infrastructure command secret resolution failed for {CommandId}: HTTP {StatusCode}",
                command.CommandId, (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }
        var body = await response.Content.ReadFromJsonAsync<InfrastructureCommandSecretsResponse>(cancellationToken);
        return ValidateSecretResolutionResponse(body);
    }

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

    public async Task<string> GetDispositionAsync(Guid operationId, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken);
        using var response = await client.GetAsync(
            $"api/host-container-executions/{operationId:D}/disposition", cancellationToken);
        response.EnsureSuccessStatusCode();
        var outcome = await response.Content.ReadFromJsonAsync<HostContainerCommandDispositionResponse>(cancellationToken);
        return outcome?.Outcome ?? throw new InvalidOperationException("The API returned no host-container command disposition.");
    }

    public async Task SubmitAsync(
        InfrastructureCommandRequestedEvent command,
        HostContainerExecutionResponse result,
        CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken);
        using var response = await InfrastructureResultDelivery.SendAsync(token => HttpClientJsonExtensions.PostAsJsonAsync(
            client,
            $"api/host-container-executions/{command.OperationId:D}/result",
            new HostContainerExecutionResultRequest(command.RunnerHostId, command.InfrastructureRunnerId, result),
            token), cancellationToken);
        response.EnsureSuccessStatusCode();
        var outcome = await response.Content.ReadFromJsonAsync<HostContainerCommandDispositionResponse>(cancellationToken);
        if (outcome?.Outcome is not ("accepted" or "already-applied"))
            throw new InvalidOperationException("The API did not persist the host-container execution result.");
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

    internal static InfrastructureCommandSecretsResponse ValidateSecretResolutionResponse(
        InfrastructureCommandSecretsResponse? response)
    {
        if (response == null)
            throw new InvalidOperationException("The API returned no infrastructure command secret resolution.");
        if (response.Disposition is not (InfrastructureCommandSecretResolutionDispositions.Executable
            or InfrastructureCommandSecretResolutionDispositions.Terminal))
            throw new InvalidOperationException("The API returned an unknown infrastructure command secret disposition.");
        if (response.Disposition == InfrastructureCommandSecretResolutionDispositions.Executable
            && response.EnvironmentVariables == null)
            throw new InvalidOperationException("The API returned no infrastructure command secrets.");
        if (response.Disposition == InfrastructureCommandSecretResolutionDispositions.Terminal
            && string.IsNullOrWhiteSpace(response.TerminalReason))
            throw new InvalidOperationException("The API returned a terminal secret disposition without a reason.");
        return response;
    }

    private sealed record InfrastructureCommandSecretsRequest(Guid OperationId, Guid RunnerHostId,
        Guid InfrastructureRunnerId, string ActionKey, IReadOnlyList<Guid> SecretGrantIds);
    private sealed record InfrastructureCommandResultRequest(Guid OperationId, Guid RunnerHostId,
        Guid InfrastructureRunnerId, string ActionKey, IReadOnlyList<Guid> SecretGrantIds,
        AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result);
}
