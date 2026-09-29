using System.Net;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Services;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

public sealed class HostedRunnerBrokerProvisionerTests
{
    [Test]
    public async Task ProvisionAsync_CreatesRunnerQueueAndConsumeOnlyCredential()
    {
        var runnerId = Guid.NewGuid();
        var (result, requests) = await ProvisionAsync(runnerId);
        var expectedQueue = $"automaton.hosted-runner.{runnerId:N}";
        Assert.That(result.QueueName, Is.EqualTo(expectedQueue));
        Assert.That(result.Host, Is.EqualTo("broker.example"));
        Assert.That(result.Username, Is.EqualTo($"hosted-runner-{runnerId:N}"));
        Assert.That(result.Password, Is.Not.Empty);
        Assert.That(requests, Has.Count.EqualTo(3));
        AssertConsumeOnlyPermission(requests[2].Body, expectedQueue);
        Assert.That(requests[0].Path, Does.Contain(Uri.EscapeDataString(expectedQueue)));
    }

    private static void AssertConsumeOnlyPermission(string body, string expectedQueue)
    {
        using var document = JsonDocument.Parse(body);
        var permission = document.RootElement;
        Assert.That(permission.GetProperty("configure").GetString(), Is.Empty);
        Assert.That(permission.GetProperty("write").GetString(), Is.Empty);
        Assert.That(permission.GetProperty("read").GetString(), Is.EqualTo($"^{System.Text.RegularExpressions.Regex.Escape(expectedQueue)}$"));
    }

    private static async Task<(Automaton.Core.Models.HostedRunnerBrokerConfiguration Result,
        List<(string Path, string Body)> Requests)> ProvisionAsync(Guid runnerId)
    {
        var handler = new RecordingHandler();
        var factory = new SingleClientFactory(new HttpClient(handler));
        var provisioner = new HostedRunnerBrokerProvisioner(
            factory,
            Options.Create(new RabbitMqOptions
            {
                Host = "rabbitmq",
                Port = 5672,
                Username = "operator",
                Password = "operator-password",
                VirtualHost = "/",
                ManagementUrl = "http://rabbitmq:15672/"
            }),
            Options.Create(new HostedRunnerRabbitMqOptions { Host = "broker.example", Port = 5672 }));
        var result = await provisioner.ProvisionAsync(runnerId, CancellationToken.None);
        return (result, handler.Requests);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.PathAndQuery, body));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
