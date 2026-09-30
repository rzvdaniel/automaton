using System.Net;
using System.Net.Http.Json;
using Automaton.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Automaton.Runner.Console.Services;

public sealed class HostContainerExecutionApiClient(
    IConfiguration configuration,
    FileStorageService storage)
{
    public async Task<HostContainerExecutionResponse> ExecuteAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var operation = request.OperationId == Guid.Empty
            ? request with { OperationId = Guid.NewGuid() }
            : request;
        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(operation.TimeoutSeconds, 30, 86_400) + 120);
        using var client = await CreateClientAsync();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Host-container operation {operation.OperationId} did not complete before its operation deadline.");

            try
            {
                var submission = await SubmitAsync(client, operation, cancellationToken);
                if (submission.Completed)
                    return RequireResult(submission);

                return RequireResult(await ReadUntilCompletedAsync(client, operation.OperationId, deadline, cancellationToken));
            }
            catch (HttpRequestException exception) when (IsTransient(exception.StatusCode))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private static async Task<HostContainerExecutionOperationResponse> SubmitAsync(
        HttpClient client,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClientJsonExtensions.PostAsJsonAsync(
            client, "api/host-container-executions", request, cancellationToken);
        EnsureRetryableStatus(response.StatusCode);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HostContainerExecutionOperationResponse>(cancellationToken)
            ?? throw new InvalidDataException("The API returned no host-container operation submission.");
    }

    private static async Task<HostContainerExecutionOperationResponse> ReadUntilCompletedAsync(
        HttpClient client,
        Guid operationId,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(
                    $"api/host-container-executions/{operationId:D}/result", cancellationToken);
                EnsureRetryableStatus(response.StatusCode);
                response.EnsureSuccessStatusCode();
                var operation = await response.Content.ReadFromJsonAsync<HostContainerExecutionOperationResponse>(cancellationToken)
                    ?? throw new InvalidDataException("The API returned no host-container operation result.");
                if (operation.Completed)
                    return operation;
            }
            catch (HttpRequestException exception) when (IsTransient(exception.StatusCode))
            {
                // A later read observes the durable operation after API recovery or slot cutover.
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        throw new TimeoutException($"Host-container operation {operationId} did not complete before its operation deadline.");
    }

    private async Task<HttpClient> CreateClientAsync()
    {
        var apiKey = await storage.LoadApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("A Hosted Runner API key is required for host-container operations.");
        var serverUrl = Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL")
            ?? await storage.LoadServerUrlAsync()
            ?? configuration["AppConfig:BaseUrl"];
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var address))
            throw new InvalidOperationException("The API server URL is required for host-container operations.");
        if (address.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Host-container operations require an HTTPS API URL.");

        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(address.AbsoluteUri.TrimEnd(Path.AltDirectorySeparatorChar)
                + Path.AltDirectorySeparatorChar)
        };
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        return client;
    }

    private static void EnsureRetryableStatus(HttpStatusCode statusCode)
    {
        if (IsTransient(statusCode))
            throw new HttpRequestException($"The host-container API returned transient HTTP {(int)statusCode}.", null, statusCode);
    }

    private static bool IsTransient(HttpStatusCode? statusCode)
        => statusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)statusCode.Value >= 500;

    private static HostContainerExecutionResponse RequireResult(HostContainerExecutionOperationResponse operation)
        => operation.Result
            ?? throw new InvalidDataException("A completed host-container operation contained no result.");
}
