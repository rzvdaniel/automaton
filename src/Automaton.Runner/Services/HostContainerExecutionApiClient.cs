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
        cancellationToken.ThrowIfCancellationRequested();
        if (DateTime.UtcNow >= deadline)
            throw new TimeoutException($"Host-container operation {operation.OperationId} did not complete before its operation deadline.");

        var submission = await SubmitAsync(client, operation, deadline, cancellationToken);
        if (submission.Completed)
            return RequireResult(submission);

        return RequireResult(await ReadUntilCompletedAsync(client, operation.OperationId, deadline, cancellationToken));
    }

    private static async Task<HostContainerExecutionOperationResponse> SubmitAsync(
        HttpClient client,
        HostContainerExecutionRequest request,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(token =>
            HttpClientJsonExtensions.PostAsJsonAsync(client, "api/host-container-executions", request, token),
            deadline, cancellationToken);
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
            using var response = await SendWithRetryAsync(token =>
                client.GetAsync($"api/host-container-executions/{operationId:D}/result", token),
                deadline, cancellationToken);
            response.EnsureSuccessStatusCode();
            var operation = await response.Content.ReadFromJsonAsync<HostContainerExecutionOperationResponse>(cancellationToken)
                ?? throw new InvalidDataException("The API returned no host-container operation result.");
            if (operation.Completed)
                return operation;
            await DelayBeforeDeadlineAsync(TimeSpan.FromMilliseconds(500), deadline, cancellationToken);
        }

        throw new TimeoutException($"Host-container operation {operationId} did not complete before its operation deadline.");
    }

    internal static async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        DateTime deadline,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response;
            try
            {
                response = await send(cancellationToken);
            }
            catch (HttpRequestException exception) when (IsTransient(exception.StatusCode))
            {
                Serilog.Log.Warning(exception, "Host-container API request failed; retrying the existing operation.");
                await DelayBeforeDeadlineAsync(TimeSpan.FromSeconds(2), deadline, cancellationToken, delay);
                continue;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Serilog.Log.Warning("Host-container API request timed out; retrying the existing operation.");
                await DelayBeforeDeadlineAsync(TimeSpan.FromSeconds(2), deadline, cancellationToken, delay);
                continue;
            }

            if (!IsTransient(response.StatusCode))
                return response;

            var retryDelay = response.StatusCode == HttpStatusCode.TooManyRequests
                ? InfrastructureResultDelivery.GetRetryDelay(response, DateTimeOffset.UtcNow)
                : TimeSpan.FromSeconds(2);
            var statusCode = response.StatusCode;
            response.Dispose();
            Serilog.Log.Warning(
                "Host-container API returned HTTP {StatusCode}; retrying the existing operation in {RetryDelay}.",
                (int)statusCode, retryDelay);
            await DelayBeforeDeadlineAsync(retryDelay, deadline, cancellationToken, delay);
        }

        throw new TimeoutException("The host-container API operation did not complete before its deadline.");
    }

    private static async Task DelayBeforeDeadlineAsync(
        TimeSpan delayDuration,
        DateTime deadline,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            throw new TimeoutException("The host-container API operation did not complete before its deadline.");

        delay ??= Task.Delay;
        if (delayDuration >= remaining)
        {
            await delay(remaining, cancellationToken);
            throw new TimeoutException("The host-container API operation did not complete before its deadline.");
        }

        await delay(delayDuration, cancellationToken);
    }

    private async Task<HttpClient> CreateClientAsync()
    {
        var apiKey = await storage.LoadApiKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("A Runner API key is required for host-container operations.");
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

    private static bool IsTransient(HttpStatusCode? statusCode)
        => statusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)statusCode.Value >= 500;

    private static HostContainerExecutionResponse RequireResult(HostContainerExecutionOperationResponse operation)
        => operation.Result
            ?? throw new InvalidDataException("A completed host-container operation contained no result.");
}
