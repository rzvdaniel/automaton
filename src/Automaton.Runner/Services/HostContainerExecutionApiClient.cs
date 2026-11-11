using System.Net;
using System.Net.Http.Json;
using Automaton.Core.Models;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Retry;

namespace Automaton.Runner.Console.Services;

public sealed class HostContainerExecutionApiClient(
    IConfiguration configuration,
    FileStorageService storage,
    HostContainerResultNotifier notifier)
{
    /// <summary>How long before the deadline the one last fallback read is made when no message arrived.</summary>
    internal static readonly TimeSpan DeadlineFallbackLead = TimeSpan.FromSeconds(30);
    /// <summary>Only for an API that offers no result queue: read the result at this fixed interval.</summary>
    internal static readonly TimeSpan LegacyReadInterval = TimeSpan.FromSeconds(5);

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

        // Register before submitting so a result pushed immediately is not missed.
        using var waiter = notifier.Register(operation.OperationId);
        var submission = await SubmitAsync(client, operation, deadline, cancellationToken);
        if (submission.Completed)
            return RequireResult(submission);

        Task<HostContainerExecutionOperationResponse> ReadAsync() => ReadOnceAsync(client, operation.OperationId, deadline, cancellationToken);
        var completed = notifier.ResultQueueAvailable
            ? await WaitForPushedResultAsync(ReadAsync, waiter.Signaled, notifier.NextConnection, deadline,
                operation.OperationId, cancellationToken: cancellationToken)
            : await ReadAtIntervalAsync(ReadAsync, deadline, operation.OperationId, cancellationToken);
        return RequireResult(completed);
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

    private static async Task<HostContainerExecutionOperationResponse> ReadOnceAsync(
        HttpClient client,
        Guid operationId,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(token =>
            client.GetAsync($"api/host-container-executions/{operationId:D}/result", token),
            deadline, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HostContainerExecutionOperationResponse>(cancellationToken)
            ?? throw new InvalidDataException("The API returned no host-container operation result.");
    }

    /// <summary>
    /// Waits for the "result ready" message, then reads the saved result once. Besides that read, the result is
    /// read once each time the result consumer reconnects (a message may have been missed while offline) and once
    /// shortly before the deadline (a purged queue). There is no polling loop.
    /// </summary>
    internal static async Task<HostContainerExecutionOperationResponse> WaitForPushedResultAsync(
        Func<Task<HostContainerExecutionOperationResponse>> read,
        Task signaled,
        Func<Task> nextConnection,
        DateTime deadline,
        Guid operationId,
        TimeProvider? time = null,
        CancellationToken cancellationToken = default,
        TimeSpan? fallbackLead = null)
    {
        time ??= TimeProvider.System;
        var lastChance = deadline - (fallbackLead ?? DeadlineFallbackLead);
        var lastChanceTaken = false;
        var connection = nextConnection();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = time.GetUtcNow().UtcDateTime;
            if (now >= deadline)
                throw new TimeoutException($"Host-container operation {operationId} did not complete before its operation deadline.");
            var wakeAt = lastChanceTaken ? deadline : (lastChance > now ? lastChance : now);
            using var timerStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timer = Task.Delay(wakeAt - now, time, timerStop.Token);
            var first = await Task.WhenAny(signaled, connection, timer);
            await timerStop.CancelAsync();
            if (first == timer)
            {
                if (lastChanceTaken)
                    continue; // the deadline check at the top throws
                lastChanceTaken = true;
            }
            else if (first == connection)
                connection = nextConnection();
            else
                signaled = new TaskCompletionSource().Task; // consumed: a read that finds nothing waits for a reconnect or the deadline

            var operation = await read();
            if (operation.Completed)
                return operation;
        }
    }

    /// <summary>For an API without a result queue only (older server): a plain fixed-interval read.</summary>
    private static async Task<HostContainerExecutionOperationResponse> ReadAtIntervalAsync(
        Func<Task<HostContainerExecutionOperationResponse>> read,
        DateTime deadline,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = await read();
            if (operation.Completed)
                return operation;
            await DelayBeforeDeadlineAsync(LegacyReadInterval, deadline, cancellationToken);
        }

        throw new TimeoutException($"Host-container operation {operationId} did not complete before its operation deadline.");
    }

    internal static async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        DateTime deadline,
        CancellationToken cancellationToken,
        TimeProvider? time = null,
        Func<double>? jitter = null)
    {
        time ??= TimeProvider.System;
        jitter ??= Random.Shared.NextDouble;
        // Transient failures are retried on the stepped schedule (2s, 5s, 15s, then every 30s) until the
        // operation deadline; a rate limit waits as long as the server asks. Other answers return at once.
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = int.MaxValue,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome switch
                {
                    { Result: { } response } => IsTransient(response.StatusCode),
                    { Exception: HttpRequestException exception } => IsTransient(exception.StatusCode),
                    { Exception: OperationCanceledException } => !cancellationToken.IsCancellationRequested,
                    _ => false
                }),
                DelayGenerator = args =>
                {
                    var delay = args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests } limited
                        ? InfrastructureResultDelivery.GetRetryDelay(limited, time.GetUtcNow())
                        : RetrySchedule.Delay(args.AttemptNumber, RetrySchedule.RequestCeiling, jitter());
                    var remaining = deadline - time.GetUtcNow().UtcDateTime;
                    // Wait out the remaining time at most; the next attempt then reports the deadline.
                    return ValueTask.FromResult<TimeSpan?>(delay < remaining ? delay : remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                },
                OnRetry = args =>
                {
                    if (args.Outcome.Result is { } response)
                    {
                        Serilog.Log.Warning(
                            "Host-container API returned HTTP {StatusCode}; retrying the existing operation in {RetryDelay}.",
                            (int)response.StatusCode, args.RetryDelay);
                        response.Dispose();
                    }
                    else
                    {
                        Serilog.Log.Warning(args.Outcome.Exception,
                            "Host-container API request failed; retrying the existing operation in {RetryDelay}.", args.RetryDelay);
                    }
                    return default;
                }
            })
            .Build();

        return await pipeline.ExecuteAsync(async token =>
        {
            if (time.GetUtcNow().UtcDateTime >= deadline)
                throw new TimeoutException("The host-container API operation did not complete before its deadline.");
            return await send(token);
        }, cancellationToken);
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
