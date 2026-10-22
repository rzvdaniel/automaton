using System.Net;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Services;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

public sealed class HostedRunnerBrokerProvisionerTests
{
    [Test]
    public async Task RevokeAsync_DeletesRunnerUserAndQueue()
    {
        var runnerId = Guid.NewGuid();
        var handler = new RecordingHandler();
        var provisioner = CreateProvisioner(handler);

        await provisioner.RevokeAsync(runnerId, CancellationToken.None);

        Assert.That(handler.Requests, Has.Count.EqualTo(5));
        Assert.That(handler.Requests[0].Path, Does.Contain($"execution-runner-{runnerId:N}"));
        Assert.That(handler.Requests[1].Path, Does.Contain(RunnerExecutionCommandPublisher.QueueName(runnerId)));
        Assert.That(handler.Requests[2].Path, Does.Contain(RunnerExecutionCommandPublisher.ManagementQueueName(runnerId)));
        Assert.That(handler.Requests[3].Method, Is.EqualTo(HttpMethod.Delete));
        Assert.That(handler.Requests[3].Path, Does.Contain($"hosted-runner-{runnerId:N}"));
        Assert.That(handler.Requests[4].Path, Does.Contain(HostedRunnerBrokerProvisioner.LegacyDeploymentQueueName(runnerId)));
    }

    [Test]
    public async Task ExecutionCredentialCanOnlyConsumeItsOwnQueue()
    {
        var runnerId = Guid.NewGuid();
        var handler = new RecordingHandler();
        var result = await CreateProvisioner(handler).ProvisionExecutionAsync(runnerId, CancellationToken.None);
        Assert.That(result.QueueName, Is.EqualTo(RunnerExecutionCommandPublisher.QueueName(runnerId)));
        Assert.That(result.Username, Is.EqualTo($"execution-runner-{runnerId:N}"));
        Assert.That(result.TlsEnabled, Is.True);
        Assert.That(result.TlsServerName, Is.EqualTo("broker-tls.example"));
        Assert.That(result.ManagementQueueName, Is.EqualTo(RunnerExecutionCommandPublisher.ManagementQueueName(runnerId)));
        Assert.That(handler.Requests[1].Path, Does.Contain(result.ManagementQueueName));
        using var permissions = JsonDocument.Parse(handler.Requests[3].Body);
        Assert.That(permissions.RootElement.GetProperty("configure").GetString(), Is.Empty);
        Assert.That(permissions.RootElement.GetProperty("write").GetString(), Is.Empty);
        var read = permissions.RootElement.GetProperty("read").GetString()!;
        Assert.That(System.Text.RegularExpressions.Regex.IsMatch(result.QueueName, read), Is.True);
        Assert.That(System.Text.RegularExpressions.Regex.IsMatch(result.ManagementQueueName!, read), Is.True);
        Assert.That(System.Text.RegularExpressions.Regex.IsMatch(RunnerExecutionCommandPublisher.ManagementQueueName(Guid.NewGuid()), read), Is.False);
    }

    [Test]
    public void ExecutionRejectsPlaintextBeforeIssuingCredentials()
    {
        var handler = new RecordingHandler();
        var provisioner = CreateProvisioner(handler, tlsEnabled: false);
        Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionExecutionAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.That(handler.Requests, Is.Empty);
    }

    private static HostedRunnerBrokerProvisioner CreateProvisioner(RecordingHandler handler, bool tlsEnabled = true)
        => new(
            new SingleClientFactory(new HttpClient(handler)),
            Options.Create(new RabbitMqOptions
            {
                Host = "rabbitmq",
                Port = 5672,
                Username = "operator",
                Password = "operator-password",
                VirtualHost = "/",
                ManagementUrl = "http://rabbitmq:15672/"
            }),
            Options.Create(new HostedRunnerRabbitMqOptions
            {
                Host = "broker.example", Port = 5671, TlsEnabled = tlsEnabled, TlsServerName = "broker-tls.example"
            }));

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
