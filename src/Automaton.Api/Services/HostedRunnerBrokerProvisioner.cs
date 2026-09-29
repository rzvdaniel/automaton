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
    public async Task<HostedRunnerBrokerConfiguration> ProvisionAsync(
        Guid runnerId,
        CancellationToken cancellationToken)
    {
        var rabbit = rabbitOptions.Value;
        var external = runnerBrokerOptions.Value;
        if (!Uri.TryCreate(rabbit.ManagementUrl, UriKind.Absolute, out var managementUrl)
            || string.IsNullOrWhiteSpace(rabbit.Username)
            || string.IsNullOrWhiteSpace(rabbit.Password)
            || string.IsNullOrWhiteSpace(external.Host)
            || external.Port <= 0)
            throw new InvalidOperationException("Hosted Runner RabbitMQ management and broker endpoints must be configured.");

        var username = $"hosted-runner-{runnerId:N}";
        var queueName = HostedRunnerDeploymentCommandPublisher.QueueName(runnerId);
        var brokerPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var client = httpClientFactory.CreateClient(nameof(HostedRunnerBrokerProvisioner));
        client.BaseAddress = new Uri(managementUrl, managementUrl.AbsoluteUri.EndsWith('/') ? "" : "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{rabbit.Username}:{rabbit.Password}")));

        var encodedVhost = Uri.EscapeDataString(rabbit.VirtualHost);
        await PutAsync(client, $"api/queues/{encodedVhost}/{Uri.EscapeDataString(queueName)}",
            new { durable = true, auto_delete = false, arguments = new { } }, cancellationToken);
        await PutAsync(client, $"api/users/{Uri.EscapeDataString(username)}",
            new { password = brokerPassword, tags = "" }, cancellationToken);
        await PutAsync(client, $"api/permissions/{encodedVhost}/{Uri.EscapeDataString(username)}",
            new { configure = "", write = "", read = $"^{System.Text.RegularExpressions.Regex.Escape(queueName)}$" }, cancellationToken);

        return new HostedRunnerBrokerConfiguration(
            external.Host, external.Port, rabbit.VirtualHost, username, brokerPassword, queueName);
    }

    private static async Task PutAsync<T>(HttpClient client, string path, T body, CancellationToken cancellationToken)
    {
        using var response = await System.Net.Http.Json.HttpClientJsonExtensions.PutAsJsonAsync(
            client, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
