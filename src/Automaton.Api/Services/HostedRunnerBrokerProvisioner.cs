using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Config;
using Automaton.Core.Models;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public sealed class HostedRunnerBrokerProvisioner(
    IHttpClientFactory httpClientFactory,
    IOptions<RabbitMqOptions> rabbitOptions,
    IOptions<HostedRunnerRabbitMqOptions> runnerBrokerOptions)
{
    public Task<RunnerBrokerConfiguration> ProvisionExecutionAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        if (!runnerBrokerOptions.Value.TlsEnabled)
            throw new InvalidOperationException("Workflow and custom-step Runner broker connections require TLS.");
        return ProvisionQueueAsync($"execution-runner-{runnerId:N}",
            RunnerExecutionCommandPublisher.QueueName(runnerId), cancellationToken,
            RunnerExecutionCommandPublisher.ManagementQueueName(runnerId), RunnerExecutionCommandPublisher.ResultQueueName(runnerId));
    }

    private async Task<RunnerBrokerConfiguration> ProvisionQueueAsync(string username,
        string queueName, CancellationToken cancellationToken, string? managementQueueName = null, string? resultQueueName = null)
    {
        var rabbit = rabbitOptions.Value;
        var external = runnerBrokerOptions.Value;
        if (!Uri.TryCreate(rabbit.ManagementUrl, UriKind.Absolute, out var managementUrl)
            || string.IsNullOrWhiteSpace(rabbit.Username)
            || string.IsNullOrWhiteSpace(rabbit.Password)
            || string.IsNullOrWhiteSpace(external.Host)
            || external.Port <= 0)
            throw new InvalidOperationException("Hosted Runner RabbitMQ management and broker endpoints must be configured.");

        var brokerPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var client = CreateManagementClient(rabbit, managementUrl);

        var encodedVhost = Uri.EscapeDataString(rabbit.VirtualHost);
        await PutAsync(client, $"api/queues/{encodedVhost}/{Uri.EscapeDataString(queueName)}",
            new { durable = true, auto_delete = false, arguments = new { } }, cancellationToken);
        if (managementQueueName is not null)
            await PutAsync(client, $"api/queues/{encodedVhost}/{Uri.EscapeDataString(managementQueueName)}",
                new { durable = true, auto_delete = false, arguments = new { } }, cancellationToken);
        // The result queue lives in the broker, not in an API slot, so a blue/green switch does not touch it.
        // Messages expire and the queue is bounded, so an older Runner that never reads it cannot fill the broker.
        if (resultQueueName is not null)
            await PutAsync(client, $"api/queues/{encodedVhost}/{Uri.EscapeDataString(resultQueueName)}",
                new { durable = true, auto_delete = false, arguments = new Dictionary<string, object> { ["x-message-ttl"] = 3_600_000, ["x-max-length"] = 5000 } }, cancellationToken);
        await PutAsync(client, $"api/users/{Uri.EscapeDataString(username)}",
            new { password = brokerPassword, tags = "" }, cancellationToken);
        await PutAsync(client, $"api/permissions/{encodedVhost}/{Uri.EscapeDataString(username)}",
            new { configure = "", write = "", read = ReadPermission(queueName, managementQueueName, resultQueueName) }, cancellationToken);

        return new RunnerBrokerConfiguration(
            external.Host, external.Port, rabbit.VirtualHost, username, brokerPassword, queueName,
            external.TlsEnabled, external.TlsServerName, managementQueueName, resultQueueName);
    }

    private static string ReadPermission(string queue, string? managementQueue, string? resultQueue)
        => "^(" + string.Join("|", new[] { queue, managementQueue, resultQueue }
            .Where(name => name is not null).Select(name => System.Text.RegularExpressions.Regex.Escape(name!))) + ")$";

    public async Task RevokeAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        var rabbit = rabbitOptions.Value;
        if (!Uri.TryCreate(rabbit.ManagementUrl, UriKind.Absolute, out var managementUrl)
            || string.IsNullOrWhiteSpace(rabbit.Username)
            || string.IsNullOrWhiteSpace(rabbit.Password))
            throw new InvalidOperationException("Hosted Runner RabbitMQ management endpoint must be configured.");

        using var client = CreateManagementClient(rabbit, managementUrl);
        var username = $"hosted-runner-{runnerId:N}";
        var encodedVhost = Uri.EscapeDataString(rabbit.VirtualHost);
        await DeleteIfPresentAsync(client, $"api/users/execution-runner-{runnerId:N}", cancellationToken);
        await DeleteIfPresentAsync(client,
            $"api/queues/{encodedVhost}/{Uri.EscapeDataString(RunnerExecutionCommandPublisher.QueueName(runnerId))}", cancellationToken);
        await DeleteIfPresentAsync(client,
            $"api/queues/{encodedVhost}/{Uri.EscapeDataString(RunnerExecutionCommandPublisher.ManagementQueueName(runnerId))}", cancellationToken);
        await DeleteIfPresentAsync(client,
            $"api/queues/{encodedVhost}/{Uri.EscapeDataString(RunnerExecutionCommandPublisher.ResultQueueName(runnerId))}", cancellationToken);
        await DeleteIfPresentAsync(client, $"api/users/{Uri.EscapeDataString(username)}", cancellationToken);
        await DeleteIfPresentAsync(client,
            $"api/queues/{encodedVhost}/{Uri.EscapeDataString(LegacyDeploymentQueueName(runnerId))}",
            cancellationToken);
    }

    public async Task RevokeExecutionAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        var rabbit = rabbitOptions.Value;
        if (!Uri.TryCreate(rabbit.ManagementUrl, UriKind.Absolute, out var managementUrl))
            throw new InvalidOperationException("Runner RabbitMQ management endpoint must be configured.");
        using var client = CreateManagementClient(rabbit, managementUrl);
        await DeleteIfPresentAsync(client, $"api/users/execution-runner-{runnerId:N}", cancellationToken);
        await DeleteIfPresentAsync(client,
            $"api/queues/{Uri.EscapeDataString(rabbit.VirtualHost)}/{Uri.EscapeDataString(RunnerExecutionCommandPublisher.QueueName(runnerId))}",
            cancellationToken);
        await DeleteIfPresentAsync(client,
            $"api/queues/{Uri.EscapeDataString(rabbit.VirtualHost)}/{Uri.EscapeDataString(RunnerExecutionCommandPublisher.ManagementQueueName(runnerId))}",
            cancellationToken);
        await DeleteIfPresentAsync(client,
            $"api/queues/{Uri.EscapeDataString(rabbit.VirtualHost)}/{Uri.EscapeDataString(RunnerExecutionCommandPublisher.ResultQueueName(runnerId))}",
            cancellationToken);
    }

    // Delete queue resources left by older Hosted Runner versions; nothing publishes to this queue now.
    public static string LegacyDeploymentQueueName(Guid runnerId) => $"automaton.hosted-runner.{runnerId:N}";

    private HttpClient CreateManagementClient(RabbitMqOptions rabbit, Uri managementUrl)
    {
        var client = httpClientFactory.CreateClient(nameof(HostedRunnerBrokerProvisioner));
        client.BaseAddress = new Uri(managementUrl, managementUrl.AbsoluteUri.EndsWith('/') ? "" : "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{rabbit.Username}:{rabbit.Password}")));
        return client;
    }

    private static async Task PutAsync<T>(HttpClient client, string path, T body, CancellationToken cancellationToken)
    {
        using var response = await System.Net.Http.Json.HttpClientJsonExtensions.PutAsJsonAsync(
            client, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task DeleteIfPresentAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await client.DeleteAsync(path, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }
}
